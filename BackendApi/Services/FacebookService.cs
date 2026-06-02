using Microsoft.Extensions.Options;
using BackendApi.Models;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http.Headers;

namespace BackendApi.Services
{
    public class FacebookService : IFacebookService
    {
        private readonly HttpClient _httpClient;
        private readonly FacebookOptions _options;
        private readonly FacebookApiCircuitBreaker _circuitBreaker;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public FacebookService(
            HttpClient httpClient,
            IOptionsSnapshot<FacebookOptions> options,
            FacebookApiCircuitBreaker circuitBreaker)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _circuitBreaker = circuitBreaker;
        }

        private static async Task ThrowFacebookApiException(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();

            FacebookApiError? facebookError = null;
            try
            {
                var envelope = JsonSerializer.Deserialize<FacebookApiErrorEnvelope>(body, JsonOptions);
                facebookError = envelope?.Error;
            }
            catch
            {
                // Ignore parsing errors; fall back to raw body.
            }

            var message = facebookError?.Message ?? body;
            throw new FacebookApiException(response.StatusCode, facebookError, body, $"Facebook API Error: {(int)response.StatusCode} - {message}");
        }

        public async Task<object?> GetPageInfoAsync(string pageId)
        {
            using var response = await SendDashboardRequestAsync(HttpMethod.Get, pageId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<object?> GetPostsAsync(string pageId)
        {
            using var response = await SendDashboardRequestAsync(HttpMethod.Get, $"{pageId}/posts");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<object?> CreatePostAsync(string pageId, CreatePostRequest request)
        {
            using var response = await SendDashboardRequestAsync(
                HttpMethod.Post,
                $"{pageId}/feed",
                new { message = request.Message });
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<bool> DeletePostAsync(string postId)
        {
            using var response = await SendDashboardRequestAsync(HttpMethod.Delete, postId);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return response.IsSuccessStatusCode;
        }

        public async Task<object?> GetCommentsAsync(string postId)
        {
            using var response = await SendDashboardRequestAsync(HttpMethod.Get, $"{postId}/comments");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<object?> GetLikesAsync(string postId)
        {
            using var response = await SendDashboardRequestAsync(HttpMethod.Get, $"{postId}/likes");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<object?> GetInsightsAsync(string pageId)
        {
            using var response = await SendDashboardRequestAsync(
                HttpMethod.Get,
                $"{pageId}/insights?metric=page_views_total");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        private async Task<HttpResponseMessage> SendDashboardRequestAsync(
            HttpMethod method,
            string url,
            object? body = null)
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _options.PageAccessToken);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return await _httpClient.SendAsync(request);
        }

        public Task HideCommentAsync(string commentId, CancellationToken ct) =>
            SendActionAsync(HttpMethod.Post, commentId, new { is_hidden = true }, ct);

        public Task ReplyToCommentAsync(string commentId, string message, CancellationToken ct) =>
            SendActionAsync(HttpMethod.Post, $"{commentId}/comments", new { message }, ct);

        public Task BlockUserAsync(string pageId, string userId, CancellationToken ct) =>
            SendActionAsync(HttpMethod.Post, $"{pageId}/blocked", new { user = userId }, ct);

        private async Task SendActionAsync(
            HttpMethod method,
            string url,
            object body,
            CancellationToken ct)
        {
            _circuitBreaker.ThrowIfOpen();
            using var request = new HttpRequestMessage(method, url)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                _options.PageAccessToken);

            using var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                try
                {
                    await ThrowFacebookApiException(response);
                }
                catch (Exception ex)
                {
                    _circuitBreaker.RecordFailure(ex);
                    throw;
                }
            }
            _circuitBreaker.RecordSuccess();
        }
    }
}
