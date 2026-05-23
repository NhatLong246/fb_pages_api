namespace CoreService.Models
{
    public class BlacklistedUser
    {
        public string UserId { get; set; } = string.Empty;
        public string PageId { get; set; } = string.Empty;
        public int SpamCount { get; set; }
        public DateTimeOffset FirstSpamAt { get; set; }
        public DateTimeOffset LastSpamAt { get; set; }
        public bool IsBlocked { get; set; }
    }
}
