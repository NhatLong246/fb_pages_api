using Confluent.Kafka;
using Microsoft.Extensions.Options;
using RetryService.Models;
using System.Text.Json;

namespace RetryService.Services
{
    public class FailedEventRetryWorker : BackgroundService
    {
        private readonly RetryOptions _options;
        private readonly ILogger<FailedEventRetryWorker> _logger;

        public FailedEventRetryWorker(
            IOptions<RetryOptions> options,
            ILogger<FailedEventRetryWorker> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!IsConfigValid())
            {
                _logger.LogWarning("[RETRY] Worker disabled because Kafka config is incomplete.");
                return;
            }

            await Task.Yield();
            await RunLoopAsync(stoppingToken);
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
                        _logger.LogError(ex, "[RETRY] Kafka consume error. Continuing.");
                        continue;
                    }

                    if (result?.Message?.Value is null) continue;

                    await ProcessFailedEventAsync(result, producer, consumer, ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[RETRY] Service stopping.");
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
                var originalCommand = doc.RootElement.GetProperty("OriginalCommand");
                var commandId = originalCommand.GetProperty("CommandId").GetString()
                              ?? result.Message.Key
                              ?? string.Empty;
                var retryCount = doc.RootElement.GetProperty("RetryCount").GetInt32();

                if (string.IsNullOrWhiteSpace(commandId))
                {
                    _logger.LogWarning(
                        "[RETRY] Skipping failed command without CommandId at {Offset}.",
                        result.TopicPartitionOffset);
                    consumer.Commit(result);
                    return;
                }

                if (RetrySchedulePolicy.IsExhausted(retryCount, _options.MaxRetryAttempts))
                {
                    await PublishDeadLetterAsync(
                        producer,
                        result.Message.Key,
                        result.Message.Value,
                        commandId,
                        retryCount,
                        ct);

                    consumer.Commit(result);
                    return;
                }

                var delay = TimeSpan.FromSeconds(RetrySchedulePolicy.GetDelaySeconds(retryCount));
                _logger.LogWarning(
                    "[RETRY] Scheduled after {DelaySeconds}s. CommandId={CommandId} RetryCount={RetryCount}",
                    delay.TotalSeconds,
                    commandId,
                    retryCount);

                await Task.Delay(delay, ct);

                await producer.ProduceAsync(
                    _options.RetryTopic,
                    new Message<string, string>
                    {
                        Key = commandId,
                        Value = WithIncrementedRetryCount(originalCommand, retryCount + 1)
                    },
                    ct);

                producer.Flush(TimeSpan.FromSeconds(5));
                consumer.Commit(result);

                _logger.LogWarning(
                    "[RETRY] Republished. CommandId={CommandId} NextRetryCount={RetryCount} Topic={Topic}",
                    commandId,
                    retryCount + 1,
                    _options.RetryTopic);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "[RETRY] Failed to process message at {Offset}.",
                    result.TopicPartitionOffset);
            }
        }

        private async Task PublishDeadLetterAsync(
            IProducer<string, string> producer,
            string? key,
            string failedPayload,
            string commandId,
            int retryCount,
            CancellationToken ct)
        {
            var payload = JsonSerializer.Serialize(new
            {
                CommandId = commandId,
                RetryCount = retryCount,
                DeadLetteredAt = DateTimeOffset.UtcNow,
                FailedMessage = JsonSerializer.Deserialize<JsonElement>(failedPayload)
            });

            await producer.ProduceAsync(
                _options.DeadLetterTopic,
                new Message<string, string>
                {
                    Key = key ?? commandId,
                    Value = payload
                },
                ct);

            producer.Flush(TimeSpan.FromSeconds(5));

            _logger.LogError(
                "[DLQ] Published. CommandId={CommandId} RetryCount={RetryCount} Topic={Topic}",
                commandId,
                retryCount,
                _options.DeadLetterTopic);
        }

        private static string WithIncrementedRetryCount(
            JsonElement command,
            int retryCount)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                command.GetRawText()) ?? [];
            values["RetryCount"] = JsonSerializer.SerializeToElement(retryCount);
            return JsonSerializer.Serialize(values);
        }

        private bool IsConfigValid() =>
            !string.IsNullOrWhiteSpace(_options.BootstrapServers) &&
            !string.IsNullOrWhiteSpace(_options.RetryTopic) &&
            !string.IsNullOrWhiteSpace(_options.FailedTopic) &&
            !string.IsNullOrWhiteSpace(_options.DeadLetterTopic) &&
            !string.IsNullOrWhiteSpace(_options.RetryGroupId);
    }
}
