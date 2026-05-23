using Confluent.Kafka;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using WebhookService.Models;

namespace WebhookService.Services;

public class KafkaEventPublisher : IKafkaEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.BootstrapServers))
        {
            throw new InvalidOperationException("Kafka:BootstrapServers is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Topic))
        {
            throw new InvalidOperationException("Kafka:Topic is not configured.");
        }

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = "webhook-service",
            EnableIdempotence = true,
            Acks = Acks.All
        };

        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }

    public async Task PublishAsync(IReadOnlyList<NormalizedFacebookEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var normalizedEvent in events)
        {
            var key = BuildMessageKey(normalizedEvent);
            var value = JsonConvert.SerializeObject(normalizedEvent);

            var delivery = await _producer.ProduceAsync(
                _options.Topic,
                new Message<string, string> { Key = key, Value = value },
                cancellationToken);

            _logger.LogInformation(
                "Kafka published eventId={EventId} topic={Topic} partition={Partition} offset={Offset}",
                normalizedEvent.EventId,
                delivery.Topic,
                delivery.Partition.Value,
                delivery.Offset.Value);
        }
    }

    private static string BuildMessageKey(NormalizedFacebookEvent normalizedEvent)
    {
        if (!string.IsNullOrWhiteSpace(normalizedEvent.CommentId))
        {
            return normalizedEvent.CommentId;
        }

        if (!string.IsNullOrWhiteSpace(normalizedEvent.PageId)
            && !string.IsNullOrWhiteSpace(normalizedEvent.PostId))
        {
            return $"{normalizedEvent.PageId}:{normalizedEvent.PostId}";
        }

        return normalizedEvent.EventId;
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
