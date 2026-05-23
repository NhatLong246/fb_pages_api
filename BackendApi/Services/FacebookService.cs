using Microsoft.Extensions.Options;
using BackendApi.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace BackendApi.Services
{
    public class FacebookService : IFacebookService
    {
        private readonly HttpClient _httpClient;
        private readonly FacebookOptions _options;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public FacebookService(HttpClient httpClient, IOptionsSnapshot<FacebookOptions> options)
        {
            _httpClient = httpClient;
            _options = options.Value;
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
            var token = System.Net.WebUtility.UrlEncode(_options.PageAccessToken);
            var response = await _httpClient.GetAsync($"{pageId}?access_token={token}");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<object?> GetPostsAsync(string pageId)
        {
            var token = System.Net.WebUtility.UrlEncode(_options.PageAccessToken);
            var response = await _httpClient.GetAsync($"{pageId}/posts?access_token={token}");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<object?> CreatePostAsync(string pageId, CreatePostRequest request)
        {
            var token = System.Net.WebUtility.UrlEncode(_options.PageAccessToken);
            var response = await _httpClient.PostAsJsonAsync($"{pageId}/feed?access_token={token}", new { message = request.Message });
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<bool> DeletePostAsync(string postId)
        {
            var token = System.Net.WebUtility.UrlEncode(_options.PageAccessToken);
            var response = await _httpClient.DeleteAsync($"{postId}?access_token={token}");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return response.IsSuccessStatusCode;
        }

        public async Task<object?> GetCommentsAsync(string postId)
        {
            var token = System.Net.WebUtility.UrlEncode(_options.PageAccessToken);
            var response = await _httpClient.GetAsync($"{postId}/comments?access_token={token}");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<object?> GetLikesAsync(string postId)
        {
            var token = System.Net.WebUtility.UrlEncode(_options.PageAccessToken);
            var response = await _httpClient.GetAsync($"{postId}/likes?access_token={token}");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }

        public async Task<object?> GetInsightsAsync(string pageId)
        {
            var token = System.Net.WebUtility.UrlEncode(_options.PageAccessToken);
            var response = await _httpClient.GetAsync($"{pageId}/insights?metric=page_views_total&access_token={token}");
            if (!response.IsSuccessStatusCode)
            {
                await ThrowFacebookApiException(response);
            }
            return await response.Content.ReadFromJsonAsync<object>();
        }
    }
}
