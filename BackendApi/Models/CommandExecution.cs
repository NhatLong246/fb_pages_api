namespace BackendApi.Models
{
    public class CommandExecution
    {
        public string CommandId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string Status { get; set; } = "processing";
        public int RetryCount { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
