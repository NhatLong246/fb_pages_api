using CoreService.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoreService.Services
{
    public class AiAnalyzer
    {
        // Timeout mỗi lần gọi API
        private const int RequestTimeoutSeconds = 10;

        // Số lần retry tối đa
        private const int MaxRetries = 2;

        // Delay giữa các lần retry (exponential backoff)
        private static readonly TimeSpan[] RetryDelays =
        [
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(3)
        ];

        // Regex strip markdown fences ```json ... ```
        private static readonly Regex FenceRegex = new(
            @"^```(?:json)?\s*|\s*```$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private readonly HttpClient _httpClient;
        private readonly ILogger<AiAnalyzer> _logger;
        private readonly string _model;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public AiAnalyzer(
            IHttpClientFactory factory,
            ILogger<AiAnalyzer> logger,
            IConfiguration configuration)
        {
            _httpClient = factory.CreateClient("gemini");
            _logger = logger;
            _model = configuration["Gemini:Model"] ?? "gemini-2.5-flash";
        }

        /// <summary>
        /// Phân tích intent + sentiment của comment.
        /// Tự động retry nếu API lỗi tạm thời.
        /// Trả về Unknown nếu tất cả retry đều thất bại.
        /// </summary>
        public async Task<AiAnalysisResult> AnalyzeAsync(
            string? message,
            CancellationToken outerCt = default)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                _logger.LogDebug("AI analysis skipped — empty message.");
                return AiAnalysisResult.Unknown;
            }

            // Truncate để tránh token quá lớn (max ~500 ký tự là đủ)
            var truncated = message.Length > 500 ? message[..500] : message;
            var fallback = ClassifyByRules(truncated);

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                // Mỗi lần gọi có timeout riêng, không bị ảnh hưởng bởi outer CT
                using var timeoutCts = new CancellationTokenSource(
                    TimeSpan.FromSeconds(RequestTimeoutSeconds));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    outerCt, timeoutCts.Token);

                try
                {
                    var result = await CallGeminiAsync(truncated, linkedCts.Token);
                    if (result.Intent == "other" && result.Sentiment == "neutral")
                    {
                        return fallback;
                    }

                    if (attempt > 0)
                    {
                        _logger.LogInformation(
                            "AI analysis succeeded on attempt {Attempt}.", attempt + 1);
                    }

                    return result;
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "AI analysis timed out (attempt {Attempt}/{Max}).",
                        attempt + 1, MaxRetries + 1);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex,
                        "AI analysis HTTP error (attempt {Attempt}/{Max}).",
                        attempt + 1, MaxRetries + 1);
                }
                catch (AiParseException ex)
                {
                    // Parse lỗi → retry không giúp ích, dùng fallback ngay
                    _logger.LogWarning(ex,
                        "AI response parse failed — using fallback.");
                    return fallback;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Unexpected AI error (attempt {Attempt}/{Max}).",
                        attempt + 1, MaxRetries + 1);
                }

                // Chờ trước khi retry (không chờ ở lần cuối)
                if (attempt < MaxRetries)
                {
                    try
                    {
                        await Task.Delay(RetryDelays[attempt], outerCt);
                    }
                    catch (OperationCanceledException)
                    {
                        // outer CT bị cancel → dừng hẳn
                        break;
                    }
                }
            }

            _logger.LogWarning(
                "AI analysis failed after {Max} attempts — using Unknown fallback.",
                MaxRetries + 1);

            return fallback;
        }

        // ── Gọi Gemini API thực sự ───────────────────────────────────────────

        private async Task<AiAnalysisResult> CallGeminiAsync(
            string message, CancellationToken ct)
        {
            var requestBody = new
            {
                system_instruction = new
                {
                    parts = new[]
                    {
                        new { text = "You are a comment classification assistant. Respond ONLY with valid JSON matching exactly this schema: {\"intent\": \"inquiry|complaint|praise|spam|neutral|other\", \"sentiment\": \"positive|neutral|negative\", \"confidence\": 0.0}. No explanation. No markdown." }
                    }
                },
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"Analyze this Facebook comment:\n\"{message}\"" }
                        }
                    }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json"
                }
            };

            using var response = await _httpClient.PostAsJsonAsync(
                $"models/{_model}:generateContent", requestBody, ct);

            // 429 Rate limit hoặc 5xx → throw để retry
            if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
            {
                throw new HttpRequestException(
                    $"Gemini API returned {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"Gemini API {(int)response.StatusCode}: {errorBody}");
            }

            // 4xx khác → không retry
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            return ParseResponse(responseBody);
        }

        // ── Parse JSON response từ Claude ────────────────────────────────────

        private AiAnalysisResult ParseResponse(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);

                // Lấy text từ candidates[0].content.parts[0].text
                var textNode = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text");

                var rawText = textNode.GetString() ?? string.Empty;

                // Strip markdown fences nếu model trả về ```json ... ```
                var cleaned = FenceRegex.Replace(rawText, string.Empty).Trim();

                var result = JsonSerializer.Deserialize<AiAnalysisResult>(cleaned, JsonOpts);

                if (result is null)
                    throw new AiParseException("Deserialized to null.");

                // Validate giá trị hợp lệ
                result.Intent = NormalizeIntent(result.Intent);
                result.Sentiment = NormalizeSentiment(result.Sentiment);
                result.Confidence = Math.Clamp(result.Confidence, 0.0, 1.0);

                return result;
            }
            catch (JsonException ex)
            {
                throw new AiParseException($"JSON parse error: {ex.Message}", ex);
            }
            catch (KeyNotFoundException ex)
            {
                throw new AiParseException($"Missing field in Gemini response: {ex.Message}", ex);
            }
        }

        // ── Normalizers để tránh model trả về giá trị không mong muốn ────────

        private static readonly HashSet<string> ValidIntents =
            ["inquiry", "complaint", "praise", "spam", "neutral", "other"];

        private static readonly HashSet<string> ValidSentiments =
            ["positive", "neutral", "negative"];

        private static string NormalizeIntent(string? raw)
            => ValidIntents.Contains(raw?.ToLowerInvariant() ?? "")
                ? raw!.ToLowerInvariant()
                : "other";

        private static string NormalizeSentiment(string? raw)
            => ValidSentiments.Contains(raw?.ToLowerInvariant() ?? "")
                ? raw!.ToLowerInvariant()
                : "neutral";

        private static AiAnalysisResult ClassifyByRules(string message)
        {
            var lower = message.ToLowerInvariant();

            if (ContainsAny(lower, ["gia bao nhieu", "bao nhieu", "gia sao", "price"]))
            {
                return new AiAnalysisResult
                {
                    Intent = "inquiry",
                    Sentiment = "neutral",
                    Confidence = 0.55
                };
            }

            if (ContainsAny(lower, ["chua nhan", "khong nhan", "ho tro", "khieu nai", "complain"]))
            {
                return new AiAnalysisResult
                {
                    Intent = "complaint",
                    Sentiment = "negative",
                    Confidence = 0.6
                };
            }

            if (ContainsAny(lower, ["hay qua", "tuyet voi", "cam on", "good", "love"]))
            {
                return new AiAnalysisResult
                {
                    Intent = "praise",
                    Sentiment = "positive",
                    Confidence = 0.6
                };
            }

            return AiAnalysisResult.Unknown;
        }

        private static bool ContainsAny(string text, IEnumerable<string> patterns)
            => patterns.Any(text.Contains);
    }
}
