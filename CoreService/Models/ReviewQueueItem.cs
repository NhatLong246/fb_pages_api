namespace CoreService.Models
{
    public class ReviewQueueItem
    {
        public string EventId { get; set; } = string.Empty;
        public string CommentId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTimeOffset QueuedAt { get; set; }
        public bool IsReviewed { get; set; }
    }
}
