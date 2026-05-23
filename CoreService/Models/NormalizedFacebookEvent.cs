using System.Text.Json;

namespace CoreService.Models
{
    public class NormalizedFacebookEvent
    {
        public string EventId { get; set; } = string.Empty;
        public string Source { get; set; } = "facebook";
        public string Object { get; set; } = "page";
        public string EventType { get; set; } = "comment.created";
        public string PageId { get; set; } = string.Empty;
        public string? PostId { get; set; }
        public string? CommentId { get; set; }
        public string? ActorId { get; set; }
        public string? Message { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public JsonElement RawEvent { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public string SchemaVersion { get; set; } = "1.0";
    }
}
