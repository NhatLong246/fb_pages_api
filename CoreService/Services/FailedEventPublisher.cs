using Confluent.Kafka;
using CoreService.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CoreService.Services
{
    public interface IFailedEventPublisher
    {
        Task PublishAsync(NormalizedFacebookEvent evt,
            string reason, CancellationToken ct);
    }

    public class FailedEventPublisher : IFailedEventPublisher, IDisposable
    {
        private readonly IProducer<string, string> _producer;
        private readonly KafkaConsumerOptions _options;
        private readonly ILogger<FailedEventPublisher> _logger;

        public FailedEventPublisher(
            IOptions<KafkaConsumerOptions> opts,
            ILogger<FailedEventPublisher> logger)
        {
            _logger = logger;
            _options = opts.Value;

            var config = new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            };

            _producer = new ProducerBuilder<string, string>(config).Build();
        }

        public async Task PublishAsync(
            NormalizedFacebookEvent evt,
            string reason,
            CancellationToken ct)
        {
            var payload = JsonSerializer.Serialize(new
            {
                OriginalEvent = evt,
                FailReason = reason,
                FailedAt = DateTimeOffset.UtcNow
            });

            await _producer.ProduceAsync(_options.FailedTopic,
                new Message<string, string>
                {
                    Key = evt.EventId,
                    Value = payload
                }, ct);

            _logger.LogWarning(
                "[RETRY] Published to send_failed. EventId={EventId} Reason={Reason}",
                evt.EventId, reason);
        }

        public void Dispose()
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
            _producer.Dispose();
        }
    }
}
