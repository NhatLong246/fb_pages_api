namespace CoreService.Models
{
    public class KafkaConsumerOptions
    {
        public string BootstrapServers { get; set; } = string.Empty;
        public string Topic { get; set; } = "raw_events";
        public string RetryTopic { get; set; } = "send_retry";
        public string FailedTopic { get; set; } = "send_failed";
        public string DeadLetterTopic { get; set; } = "dead_letter";
        public string GroupId { get; set; } = "core-service";
        public string RetryGroupId { get; set; } = "core-service-retry";
        public string AutoOffsetReset { get; set; } = "Earliest";
        public int MaxRetryAttempts { get; set; } = 3;
    }
}
