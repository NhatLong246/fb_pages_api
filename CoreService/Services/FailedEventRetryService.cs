using Confluent.Kafka;
using CoreService.Data;
using CoreService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CoreService.Services
{
    public class FailedEventRetryService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly KafkaConsumerOptions _options;
        private readonly ILogger<FailedEventRetryService> _logger;

        public FailedEventRetryService(
            IServiceScopeFactory scopeFactory,
            IOptions<KafkaConsumerOptions> options,
            ILogger<FailedEventRetryService> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!IsConfigValid())
            {
                _logger.LogWarning(
                    "FailedEventRetryService disabled — Kafka config incomplete.");
                return Task.CompletedTask;
            }

            return RunLoopAsync(stoppingToken);
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                GroupId = _options.RetryGroupId,
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Earliest
            };

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            };

            using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
            using var producer = new ProducerBuilder<string, string>(producerConfig).Build();

            consumer.Subscribe(_options.FailedTopic);

            _logger.LogInformation(
                "[RETRY] Service started. FailedTopic={FailedTopic} RetryTopic={RetryTopic} DeadLetterTopic={DeadLetterTopic}",
                _options.FailedTopic,
                _options.RetryTopic,
                _options.DeadLetterTopic);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    ConsumeResult<string, string>? result;

                    try
                    {
                        result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Kafka retry consume error — continuing.");
                        continue;
                    }

                    if (result?.Message?.Value is null) continue;

                    await ProcessFailedEventAsync(result, producer, consumer, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Failed event retry service stopping.");
            }
            finally
            {
                producer.Flush(TimeSpan.FromSeconds(5));
                consumer.Close();
            }
        }

        private async Task ProcessFailedEventAsync(
            ConsumeResult<string, string> result,
            IProducer<string, string> producer,
            IConsumer<string, string> consumer,
            CancellationToken ct)
        {
            try
            {
                using var doc = JsonDocument.Parse(result.Message.Value);
                var originalEvent = doc.RootElement.GetProperty("OriginalEvent");
                var eventId = originalEvent.GetProperty("EventId").GetString()
                              ?? result.Message.Key
                              ?? string.Empty;

                if (string.IsNullOrWhiteSpace(eventId))
                {
                    _logger.LogWarning(
                        "Skipping failed event without EventId at {Offset}.",
                        result.TopicPartitionOffset);
                    consumer.Commit(result);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
                var retryCount = db.EventStates
                    .AsNoTracking()
                    .Where(e => e.EventId == eventId)
                    .Select(e => e.RetryCount)
                    .FirstOrDefault();

                if (retryCount >= _options.MaxRetryAttempts)
                {
                    await PublishDeadLetterAsync(producer, result.Message.Key, result.Message.Value, eventId, retryCount, ct);
                    _logger.LogError(
                        "[DLQ] Retry limit reached. EventId={EventId} RetryCount={RetryCount} Topic={Topic}",
                        eventId,
                        retryCount,
                        _options.DeadLetterTopic);
                    consumer.Commit(result);
                    return;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, Math.Max(retryCount - 1, 0)));
                _logger.LogWarning(
                    "[RETRY] Scheduled after {DelaySeconds}s. EventId={EventId} RetryCount={RetryCount}",
                    delay.TotalSeconds,
                    eventId,
                    retryCount);

                await Task.Delay(delay, ct);

                var payload = originalEvent.GetRawText();
                await producer.ProduceAsync(_options.RetryTopic, new Message<string, string>
                {
                    Key = eventId,
                    Value = payload
                }, ct);

                producer.Flush(TimeSpan.FromSeconds(5));
                consumer.Commit(result);

                _logger.LogWarning(
                    "[RETRY] Republished. EventId={EventId} RetryCount={RetryCount} Topic={Topic}",
                    eventId,
                    retryCount,
                    _options.RetryTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to process retry message at {Offset}.",
                    result.TopicPartitionOffset);
            }
        }

        private async Task PublishDeadLetterAsync(
            IProducer<string, string> producer,
            string? key,
            string failedPayload,
            string eventId,
            int retryCount,
            CancellationToken ct)
        {
            var payload = JsonSerializer.Serialize(new
            {
                EventId = eventId,
                RetryCount = retryCount,
                DeadLetteredAt = DateTimeOffset.UtcNow,
                FailedMessage = JsonSerializer.Deserialize<JsonElement>(failedPayload)
            });

            await producer.ProduceAsync(_options.DeadLetterTopic,
                new Message<string, string>
                {
                    Key = key ?? eventId,
                    Value = payload
                }, ct);

            producer.Flush(TimeSpan.FromSeconds(5));

            _logger.LogError(
                "[DLQ] Published. EventId={EventId} RetryCount={RetryCount} Topic={Topic}",
                eventId,
                retryCount,
                _options.DeadLetterTopic);
        }

        private bool IsConfigValid() =>
            !string.IsNullOrWhiteSpace(_options.BootstrapServers) &&
            !string.IsNullOrWhiteSpace(_options.Topic) &&
            !string.IsNullOrWhiteSpace(_options.RetryTopic) &&
            !string.IsNullOrWhiteSpace(_options.FailedTopic) &&
            !string.IsNullOrWhiteSpace(_options.DeadLetterTopic) &&
            !string.IsNullOrWhiteSpace(_options.RetryGroupId);
    }
}
