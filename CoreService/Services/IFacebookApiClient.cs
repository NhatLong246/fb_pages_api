namespace CoreService.Services
{
    public interface IFacebookApiClient
    {
        /// <summary>?n m?t comment (is_hidden = true).</summary>
        Task<bool> HideCommentAsync(string commentId, CancellationToken ct = default);

        /// <summary>Hi?n l?i comment dã ?n.</summary>
        Task<bool> UnhideCommentAsync(string commentId, CancellationToken ct = default);

        /// <summary>Xóa h?n m?t comment.</summary>
        Task<bool> DeleteCommentAsync(string commentId, CancellationToken ct = default);

        /// <summary>Block user kh?i Page (yêu c?u quy?n MODERATE).</summary>
        Task<bool> BlockUserAsync(string pageId, string userId, CancellationToken ct = default);

        /// <summary>Unblock user.</summary>
        Task<bool> UnblockUserAsync(string pageId, string userId, CancellationToken ct = default);
    }
}
