namespace CoreService.Models
{
    public class KafkaConsumerOptions
    {
        public string BootstrapServers { get; set; } = string.Empty;
        public string Topic { get; set; } = "raw_events";
        public string CommandTopic { get; set; } = "reply_commands";
        public string GroupId { get; set; } = "core-service";
        public string AutoOffsetReset { get; set; } = "Earliest";
    }
}
