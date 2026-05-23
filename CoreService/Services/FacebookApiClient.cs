using CoreService.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CoreService.Services
{
    public class FacebookApiClient : IFacebookApiClient
    {
        private readonly HttpClient _http;
        private readonly FacebookClientOptions _opts;
        private readonly ILogger<FacebookApiClient> _logger;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public FacebookApiClient(
            HttpClient http,
            IOptionsSnapshot<FacebookClientOptions> opts,
            ILogger<FacebookApiClient> logger)
        {
            _http = http;
            _opts = opts.Value;
            _logger = logger;
        }

        // -- Hide / Unhide ----------------------------------------------------

        public Task<bool> HideCommentAsync(string commentId, CancellationToken ct = default)
            => SetCommentHiddenAsync(commentId, hidden: true, ct);

        public Task<bool> UnhideCommentAsync(string commentId, CancellationToken ct = default)
            => SetCommentHiddenAsync(commentId, hidden: false, ct);

        private async Task<bool> SetCommentHiddenAsync(
            string commentId, bool hidden, CancellationToken ct)
        {
            // POST /{comment-id}?access_token=...
            // body: { "is_hidden": true/false }
            var token = EncodeToken();
            var url = $"{commentId}?access_token={token}";

            var body = new { is_hidden = hidden };
            var response = await _http.PostAsJsonAsync(url, body, ct);

            if (!response.IsSuccessStatusCode)
            {
                var ex = await BuildException(response);
                _logger.LogError(ex,
                    "HideComment failed. CommentId={CommentId} Hidden={Hidden}",
                    commentId, hidden);
                throw ex;
            }

            _logger.LogInformation(
                "Comment {Action}. CommentId={CommentId}",
                hidden ? "hidden" : "unhidden", commentId);

            return true;
        }

        // -- Delete Comment ---------------------------------------------------

        public async Task<bool> DeleteCommentAsync(string commentId, CancellationToken ct = default)
        {
            var token = EncodeToken();
            var url = $"{commentId}?access_token={token}";

            var response = await _http.DeleteAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var ex = await BuildException(response);
                _logger.LogError(ex,
                    "DeleteComment failed. CommentId={CommentId}", commentId);
                throw ex;
            }

            _logger.LogInformation("Comment deleted. CommentId={CommentId}", commentId);
            return true;
        }

        // -- Block / Unblock User ---------------------------------------------

        public async Task<bool> BlockUserAsync(
            string pageId, string userId, CancellationToken ct = default)
        {
            // POST /{page-id}/blocked?access_token=...
            // body: { "user": "<userId>" }
            // Yêu c?u quy?n: pages_manage_engagement ho?c MODERATE
            var token = EncodeToken();
            var url = $"{pageId}/blocked?access_token={token}";

            var body = new { user = userId };
            var response = await _http.PostAsJsonAsync(url, body, ct);

            if (!response.IsSuccessStatusCode)
            {
                var ex = await BuildException(response);
                _logger.LogError(ex,
                    "BlockUser failed. PageId={PageId} UserId={UserId}", pageId, userId);
                throw ex;
            }

            _logger.LogWarning(
                "User blocked from page. PageId={PageId} UserId={UserId}", pageId, userId);
            return true;
        }

        public async Task<bool> UnblockUserAsync(
            string pageId, string userId, CancellationToken ct = default)
        {
            // DELETE /{page-id}/blocked?uid=<userId>&access_token=...
            var token = EncodeToken();
            var url = $"{pageId}/blocked?uid={userId}&access_token={token}";

            var response = await _http.DeleteAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                var ex = await BuildException(response);
                _logger.LogError(ex,
                    "UnblockUser failed. PageId={PageId} UserId={UserId}", pageId, userId);
                throw ex;
            }

            _logger.LogInformation(
                "User unblocked. PageId={PageId} UserId={UserId}", pageId, userId);
            return true;
        }

        // -- Helpers ----------------------------------------------------------

        private string EncodeToken()
            => System.Net.WebUtility.UrlEncode(_opts.PageAccessToken);

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
            catch { /* ignore parse failure */ }

            var msg = err?.Message ?? raw;
            return new FacebookApiCallException(
                response.StatusCode, err,
                $"Facebook API {(int)response.StatusCode}: {msg}");
        }
    }
}
