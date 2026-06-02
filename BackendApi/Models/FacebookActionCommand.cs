namespace BackendApi.Models
{
    public class FacebookActionCommand
    {
        public string CommandId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public DecisionAction Action { get; set; }
        public string? PageId { get; set; }
        public string? CommentId { get; set; }
        public string? ActorId { get; set; }
        public string? Message { get; set; }
        public int RetryCount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public enum DecisionAction
    {
        None,
        HideComment,
        BlacklistUser,
        QueueForReview,
        BlockUser,
        ReplyPositive,
        ReplyNegative
    }
}
