using BackendApi.Data;
using BackendApi.Models;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace BackendApi.Services
{
    public class FacebookActionWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly KafkaOptions _options;
        private readonly ILogger<FacebookActionWorker> _logger;

        public FacebookActionWorker(
            IServiceScopeFactory scopeFactory,
            IOptions<KafkaOptions> options,
            ILogger<FacebookActionWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await EnsureDatabaseAsync(stoppingToken);

            using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                GroupId = _options.GroupId,
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Earliest
            }).Build();
            using var producer = new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _options.BootstrapServers,
                EnableIdempotence = true,
                Acks = Acks.All
            }).Build();

            consumer.Subscribe([_options.CommandTopic, _options.RetryTopic]);
            _logger.LogInformation(
                "[BACKEND] Command worker started. Topics={CommandTopic},{RetryTopic}",
                _options.CommandTopic,
                _options.RetryTopic);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    if (result?.Message?.Value is null) continue;
                    await ProcessAsync(result, producer, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[BACKEND] Command consume loop failed.");
                }
            }
        }

        private async Task EnsureDatabaseAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
            await db.EnsureCommandTableAsync(ct);
        }

        private async Task ProcessAsync(
            ConsumeResult<string, string> result,
            IProducer<string, string> producer,
            CancellationToken ct)
        {
            var command = JsonSerializer.Deserialize<FacebookActionCommand>(
                result.Message.Value,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (command is null || string.IsNullOrWhiteSpace(command.CommandId))
            {
                _logger.LogWarning("[BACKEND] Ignored malformed command at {Offset}.", result.TopicPartitionOffset);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
            var facebook = scope.ServiceProvider.GetRequiredService<IFacebookService>();
            var execution = await db.CommandExecutions
                .FirstOrDefaultAsync(item => item.CommandId == command.CommandId, ct);

            if (execution?.Status == "succeeded")
            {
                _logger.LogInformation("[IDEMPOTENCY] Duplicate skipped. CommandId={CommandId}", command.CommandId);
                return;
            }

            execution ??= new CommandExecution
            {
                CommandId = command.CommandId,
                EventId = command.EventId
            };
            if (db.Entry(execution).State == EntityState.Detached)
            {
                db.CommandExecutions.Add(execution);
            }
            execution.Status = "processing";
            execution.RetryCount = command.RetryCount;
            execution.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            try
            {
                await ExecuteFacebookActionAsync(facebook, command, ct);
                execution.Status = "succeeded";
                execution.ErrorMessage = null;
                execution.UpdatedAt = DateTimeOffset.UtcNow;
                await db.UpdateEventStateAsync(
                    command.EventId,
                    IsReply(command.Action) ? "replied" : "processed",
                    null,
                    ct);
                await db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "[BACKEND] Action succeeded. CommandId={CommandId} Action={Action}",
                    command.CommandId,
                    command.Action);
            }
            catch (Exception ex)
            {
                execution.Status = "failed";
                execution.ErrorMessage = ex.Message;
                execution.UpdatedAt = DateTimeOffset.UtcNow;
                await db.UpdateEventStateAsync(command.EventId, "failed", ex.Message, ct);
                await db.SaveChangesAsync(ct);

                var retryable = IsRetryable(ex);
                await PublishFailureAsync(producer, command, ex.Message, retryable, ct);
                _logger.LogError(
                    ex,
                    "[BACKEND] Action failed. CommandId={CommandId} Retryable={Retryable}",
                    command.CommandId,
                    retryable);
            }
        }

        private static Task ExecuteFacebookActionAsync(
            IFacebookService facebook,
            FacebookActionCommand command,
            CancellationToken ct)
        {
            return command.Action switch
            {
                DecisionAction.HideComment => facebook.HideCommentAsync(command.CommentId!, ct),
                DecisionAction.ReplyPositive or DecisionAction.ReplyNegative =>
                    facebook.ReplyToCommentAsync(command.CommentId!, command.Message!, ct),
                DecisionAction.BlockUser => facebook.BlockUserAsync(command.PageId!, command.ActorId!, ct),
                _ => Task.CompletedTask
            };
        }

        private async Task PublishFailureAsync(
            IProducer<string, string> producer,
            FacebookActionCommand command,
            string error,
            bool retryable,
            CancellationToken ct)
        {
            var payload = JsonSerializer.Serialize(new
            {
                OriginalCommand = command,
                RetryCount = command.RetryCount,
                Retryable = retryable,
                Error = error,
                FailedAt = DateTimeOffset.UtcNow
            });
            var topic = retryable ? _options.FailedTopic : _options.DeadLetterTopic;
            await producer.ProduceAsync(
                topic,
                new Message<string, string> { Key = command.CommandId, Value = payload },
                ct);
            _logger.LogWarning(
                retryable ? "[RETRY] Published to {Topic}. CommandId={CommandId}" : "[DLQ] Permanent failure published to {Topic}. CommandId={CommandId}",
                topic,
                command.CommandId);
        }

        private static bool IsRetryable(Exception ex) =>
            ex is HttpRequestException ||
            ex is TaskCanceledException ||
            ex is FacebookApiException fb && FacebookFailureClassifier.IsRetryable(fb.UpstreamStatusCode);

        private static bool IsReply(DecisionAction action) =>
            action is DecisionAction.ReplyPositive or DecisionAction.ReplyNegative;
    }
}
