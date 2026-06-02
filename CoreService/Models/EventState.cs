namespace CoreService.Models
{
    public enum ProcessingStatus
    {
        Received,
        Processing,
        Processed,
        Replied,
        Failed,
        Skipped
    }

    public class EventState
    {
        public string EventId { get; set; } = string.Empty;
        public ProcessingStatus Status { get; set; } = ProcessingStatus.Received;
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; } = 0;
        public DateTimeOffset ReceivedAt { get; set; }
        public DateTimeOffset? ProcessedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        // AI analysis results
        public string? Intent { get; set; }
        public string? Sentiment { get; set; }
        public bool IsSpam { get; set; }
        public SpamSeverity SpamSeverity { get; set; }
        public DecisionAction ActionTaken { get; set; }

        public string? ActorId { get; set; }          
        public string? PageId { get; set; }           
        public string? CommentId { get; set; }        
        public string? DecisionReason { get; set; }
    }

    public enum SpamSeverity { None, Light, Repeated, Harmful }

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
