namespace RetryService.Models
{
    public class RetryOptions
    {
        public string BootstrapServers { get; set; } = string.Empty;
        public string RetryTopic { get; set; } = "send_retry";
        public string FailedTopic { get; set; } = "send_failed";
        public string DeadLetterTopic { get; set; } = "dead_letter";
        public string RetryGroupId { get; set; } = "retry-service";
        public int MaxRetryAttempts { get; set; } = 3;
    }
}
