namespace BackendApi.Models
{
    public class KafkaOptions
    {
        public string BootstrapServers { get; set; } = "localhost:9092";
        public string CommandTopic { get; set; } = "reply_commands";
        public string RetryTopic { get; set; } = "send_retry";
        public string FailedTopic { get; set; } = "send_failed";
        public string DeadLetterTopic { get; set; } = "dead_letter";
        public string GroupId { get; set; } = "backend-api";
    }
}
