using BackendApi.Models;

namespace BackendApi.Services
{
    public interface IFacebookService
    {
        Task<object?> GetPageInfoAsync(string pageId);
        Task<object?> GetPostsAsync(string pageId);
        Task<object?> CreatePostAsync(string pageId, CreatePostRequest request);
        Task<bool> DeletePostAsync(string postId);
        Task<object?> GetCommentsAsync(string postId);
        Task<object?> GetLikesAsync(string postId);
        Task<object?> GetInsightsAsync(string pageId);
        Task HideCommentAsync(string commentId, CancellationToken ct);
        Task ReplyToCommentAsync(string commentId, string message, CancellationToken ct);
        Task BlockUserAsync(string pageId, string userId, CancellationToken ct);
    }
}
