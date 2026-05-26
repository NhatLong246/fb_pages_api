using CoreService.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CoreService.Services
{
    public class FacebookApiClient : IFacebookApiClient
    {
        private readonly HttpClient _http;
        private readonly FacebookClientOptions _opts;
        private readonly ILogger<FacebookApiClient> _logger;
        private readonly FacebookApiCircuitBreaker _circuitBreaker;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public FacebookApiClient(
            HttpClient http,
            IOptionsSnapshot<FacebookClientOptions> opts,
            ILogger<FacebookApiClient> logger,
            FacebookApiCircuitBreaker circuitBreaker)
        {
            _http = http;
            _opts = opts.Value;
            _logger = logger;
            _circuitBreaker = circuitBreaker;
        }

        public Task<bool> HideCommentAsync(string commentId, CancellationToken ct = default)
            => SetCommentHiddenAsync(commentId, hidden: true, ct);

        public Task<bool> UnhideCommentAsync(string commentId, CancellationToken ct = default)
            => SetCommentHiddenAsync(commentId, hidden: false, ct);

        private async Task<bool> SetCommentHiddenAsync(
            string commentId, bool hidden, CancellationToken ct)
        {
            var body = new { is_hidden = hidden };
            using var request = CreateRequest(HttpMethod.Post, commentId, body);
            using var response = await SendWithCircuitAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var ex = await BuildException(response);
                _circuitBreaker.RecordFailure(ex);
                _logger.LogError(ex,
                    "[ACTION] HideComment failed. CommentId={CommentId} Hidden={Hidden} Status={StatusCode}",
                    commentId, hidden, (int)response.StatusCode);
                throw ex;
            }

            _logger.LogInformation(
                "[ACTION] {Action}. CommentId={CommentId}",
                hidden ? "HideComment succeeded" : "UnhideComment succeeded",
                commentId);

            return true;
        }

        public async Task<bool> DeleteCommentAsync(string commentId, CancellationToken ct = default)
        {
            using var request = CreateRequest(HttpMethod.Delete, commentId);
            using var response = await SendWithCircuitAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var ex = await BuildException(response);
                _circuitBreaker.RecordFailure(ex);
                _logger.LogError(ex,
                    "[ACTION] DeleteComment failed. CommentId={CommentId} Status={StatusCode}",
                    commentId, (int)response.StatusCode);
                throw ex;
            }

            _logger.LogInformation("[ACTION] DeleteComment succeeded. CommentId={CommentId}", commentId);
            return true;
        }

        public async Task<bool> BlockUserAsync(
            string pageId, string userId, CancellationToken ct = default)
        {
            var body = new { user = userId };
            using var request = CreateRequest(HttpMethod.Post, $"{pageId}/blocked", body);
            using var response = await SendWithCircuitAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var ex = await BuildException(response);
                _circuitBreaker.RecordFailure(ex);
                _logger.LogError(ex,
                    "[ACTION] BlockUser failed. PageId={PageId} UserId={UserId} Status={StatusCode}",
                    pageId, userId, (int)response.StatusCode);
                throw ex;
            }

            _logger.LogWarning(
                "[ACTION] BlockUser succeeded. PageId={PageId} UserId={UserId}",
                pageId, userId);
            return true;
        }

        public async Task<bool> UnblockUserAsync(
            string pageId, string userId, CancellationToken ct = default)
        {
            using var request = CreateRequest(HttpMethod.Delete, $"{pageId}/blocked?uid={userId}");
            using var response = await SendWithCircuitAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var ex = await BuildException(response);
                _circuitBreaker.RecordFailure(ex);
                _logger.LogError(ex,
                    "[ACTION] UnblockUser failed. PageId={PageId} UserId={UserId} Status={StatusCode}",
                    pageId, userId, (int)response.StatusCode);
                throw ex;
            }

            _logger.LogInformation(
                "[ACTION] UnblockUser succeeded. PageId={PageId} UserId={UserId}",
                pageId, userId);
            return true;
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url, object? body = null)
        {
            var request = new HttpRequestMessage(method, url);

            if (!string.IsNullOrWhiteSpace(_opts.PageAccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    _opts.PageAccessToken);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return request;
        }

        private async Task<HttpResponseMessage> SendWithCircuitAsync(
            HttpRequestMessage request,
            CancellationToken ct)
        {
            _circuitBreaker.ThrowIfOpen();

            try
            {
                var response = await _http.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    _circuitBreaker.RecordSuccess();
                }

                return response;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _circuitBreaker.RecordFailure(ex);
                throw;
            }
        }

        private static async Task<FacebookApiCallException> BuildException(
            HttpResponseMessage response)
        {
            var raw = await response.Content.ReadAsStringAsync();
            FbError? err = null;

            try
            {
                var envelope = JsonSerializer.Deserialize<FbErrorEnvelope>(raw, JsonOpts);
                err = envelope?.Error;
            }
            catch
            {
                // Keep the raw body when Facebook returns a non-standard error.
            }

            var msg = err?.Message ?? raw;
            return new FacebookApiCallException(
                response.StatusCode, err,
                $"Facebook API {(int)response.StatusCode}: {msg}");
        }
    }
}
