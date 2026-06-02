using Confluent.Kafka;
using CoreService.Data;
using CoreService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Threading.Channels;

namespace CoreService.Services
{
    public class CoreEventConsumerService : BackgroundService
    {
        // S? worker song song x? lý pipeline
        // N?u AI call m?t ~2s, 5 workers = ~2-3 events/s throughput
        private const int WorkerCount = 5;

        // Buffer channel: 1000 slot = d? h?p th? burst ng?n
        // N?u channel d?y ? Kafka consumer t? ch? (back-pressure)
        private const int ChannelCapacity = 1000;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly KafkaConsumerOptions _kafkaOpts;
        private readonly ILogger<CoreEventConsumerService> _logger;

        // Channel trung gian gi?a Kafka thread và worker threads
        // Item: (ConsumeResult d? commit sau, deserialized event)
        private Channel<ChannelItem> _channel = null!;

        // Worker báo offset dã x? lý xong vào channel này.
        // Kafka consumer thread s? commit d? tránh g?i consumer t? nhi?u thread.
        private Channel<TopicPartitionOffset> _commitChannel = null!;

        public CoreEventConsumerService(
            IServiceScopeFactory scopeFactory,
            IOptions<KafkaConsumerOptions> kafkaOpts,
            ILogger<CoreEventConsumerService> logger)
        {
            _scopeFactory = scopeFactory;
            _kafkaOpts = kafkaOpts.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!IsConfigValid())
            {
                _logger.LogWarning(
                    "CoreEventConsumerService disabled — Kafka config incomplete.");
                return;
            }

            // Kh?i t?o bounded channel
            _channel = Channel.CreateBounded<ChannelItem>(new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait, // block producer khi d?y
                SingleWriter = true,   // ch? 1 Kafka thread ghi
                SingleReader = false   // nhi?u worker d?c
            });

            _commitChannel = Channel.CreateUnbounded<TopicPartitionOffset>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });

            // Ch?y song song: 1 producer (Kafka) + N workers
            var producerTask = Task.Run(() => KafkaProducerLoop(stoppingToken), stoppingToken);

            var workerTasks = Enumerable.Range(0, WorkerCount)
                .Select(i => Task.Run(() => WorkerLoop(i, stoppingToken), stoppingToken))
                .ToArray();

            await Task.WhenAll(new[] { producerTask }.Concat(workerTasks));
        }

        // -- Kafka Producer Loop -----------------------------------------------
        // Ch? làm 1 vi?c: d?c t? Kafka ? ghi vào channel
        // Không x? lý gì thêm ? không bao gi? b? slow

        private async Task KafkaProducerLoop(CancellationToken ct)
        {
            var config = BuildConsumerConfig();
            using var consumer = new ConsumerBuilder<string, string>(config).Build();
            var topics = new[] { _kafkaOpts.Topic };
            consumer.Subscribe(topics);
            var commitStates = new Dictionary<TopicPartition, PartitionCommitState>();

            _logger.LogInformation(
                "[CORE] Kafka consumer started. Topics={Topics} GroupId={GroupId} Workers={Workers}",
                string.Join(",", topics), _kafkaOpts.GroupId, WorkerCount);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    DrainCommits(consumer, commitStates);

                    ConsumeResult<string, string> result;

                    try
                    {
                        // Consume v?i timeout ng?n d? ki?m tra CT thu?ng xuyên
                        result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Kafka consume error — continuing.");
                        continue;
                    }

                    if (result?.Message?.Value is null) continue;

                    // Deserialize ngay t?i dây d? skip message l?i s?m
                    NormalizedFacebookEvent? evt;
                    try
                    {
                        evt = JsonSerializer.Deserialize<NormalizedFacebookEvent>(
                            result.Message.Value,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex,
                            "Skipping malformed message at {Offset}.",
                            result.TopicPartitionOffset);
                        consumer.Commit(result); // commit d? không stuck
                        continue;
                    }

                    if (evt is null || string.IsNullOrWhiteSpace(evt.EventId))
                    {
                        _logger.LogWarning(
                            "Skipping event with missing EventId at {Offset}.",
                            result.TopicPartitionOffset);
                        consumer.Commit(result);
                        continue;
                    }

                    EnsureCommitState(commitStates, result.TopicPartition, result.Offset.Value);

                    // Ghi vào channel — await s? block n?u channel d?y (back-pressure)
                    // Ði?u này ?n: Kafka consumer ch?, KHÔNG m?t event
                    await _channel.Writer.WriteAsync(new ChannelItem(result, evt), ct);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Kafka producer loop stopping.");
            }
            finally
            {
                _channel.Writer.Complete();
                DrainCommits(consumer, commitStates);
                consumer.Close();
                _logger.LogInformation("Kafka consumer closed.");
            }
        }

        // -- Worker Loop -------------------------------------------------------
        // M?i worker d?c t? channel ? ch?y pipeline ? commit offset

        private async Task WorkerLoop(int workerId, CancellationToken ct)
        {
            _logger.LogInformation("Worker {WorkerId} started.", workerId);

            await foreach (var item in _channel.Reader.ReadAllAsync(ct))
            {
                await ProcessWithStateTrackingAsync(
                    item.ConsumeResult, item.Event, workerId, ct);
            }

            _logger.LogInformation("Worker {WorkerId} stopped.", workerId);
        }

        // -- Pipeline chính ---------------------------------------------------

        private async Task ProcessWithStateTrackingAsync(
            ConsumeResult<string, string> kafkaResult,
            NormalizedFacebookEvent evt,
            int workerId,
            CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;

            var db = sp.GetRequiredService<CoreDbContext>();
            var rateLimitService = sp.GetRequiredService<RateLimitService>();
            var spamDetector = sp.GetRequiredService<SpamDetector>();
            var aiAnalyzer = sp.GetRequiredService<AiAnalyzer>();
            var decisionEngine = sp.GetRequiredService<DecisionEngine>();
            var actionExecutor = sp.GetRequiredService<ActionExecutor>();

            // -- Ch?ng x? lý trùng (idempotency) -----------------------------
            var existing = await db.EventStates
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.EventId == evt.EventId, ct);

            if (existing?.Status is ProcessingStatus.Processed or ProcessingStatus.Replied)
            {
                _logger.LogDebug(
                    "Duplicate event skipped. EventId={EventId}", evt.EventId);
                CommitOffset(kafkaResult);
                return;
            }

            // -- T?o/c?p nh?t EventState ? Received --------------------------
            var state = existing ?? new EventState
            {
                EventId = evt.EventId,
                ActorId = evt.ActorId,
                PageId = evt.PageId,
                CommentId = evt.CommentId,
                ReceivedAt = DateTimeOffset.UtcNow
            };

            state.Status = ProcessingStatus.Processing;
            state.UpdatedAt = DateTimeOffset.UtcNow;

            var shouldReuseDecision = existing is not null
                && existing.RetryCount > 0
                && existing.ActionTaken != DecisionAction.None;

            if (!shouldReuseDecision)
            {
                var rateLimit = await rateLimitService.CheckAsync(evt, ct);
                if (rateLimit.IsLimited)
                {
                    state.IsSpam = false;
                    state.SpamSeverity = SpamSeverity.None;
                    state.Intent = "pending_review";
                    state.Sentiment = "neutral";
                    state.ActionTaken = DecisionAction.QueueForReview;
                    state.DecisionReason = rateLimit.Reason;
                    state.Status = ProcessingStatus.Processed;
                    state.ProcessedAt = DateTimeOffset.UtcNow;
                    state.UpdatedAt = DateTimeOffset.UtcNow;

                    await EnqueueReviewAsync(db, evt, "rate_limit_exceeded", ct);

                    if (existing is null)
                        db.EventStates.Add(state);
                    else
                        db.EventStates.Update(state);

                    await db.SaveChangesAsync(ct);

                    _logger.LogWarning(
                        "[RATE_LIMIT] Exceeded. EventId={EventId} ActorId={ActorId} Count={Count}/{Limit} WindowSeconds={WindowSeconds}",
                        evt.EventId,
                        evt.ActorId,
                        rateLimit.CurrentCount,
                        rateLimit.Limit,
                        rateLimit.WindowSeconds);
                    _logger.LogInformation(
                        "[EVENT] Processed. Worker={WorkerId} EventId={EventId} Action={Action} Status={Status}",
                        workerId,
                        evt.EventId,
                        state.ActionTaken,
                        state.Status);

                    CommitOffset(kafkaResult);
                    return;
                }
            }

            if (existing is null)
                db.EventStates.Add(state);
            else
                db.EventStates.Update(state);

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[EVENT] Received. Worker={WorkerId} EventId={EventId} CommentId={CommentId} ActorId={ActorId} Message=\"{Message}\"",
                workerId, evt.EventId, evt.CommentId, evt.ActorId, Summarize(evt.Message));

            try
            {
                DecisionResult decision;
                if (shouldReuseDecision)
                {
                    decision = new DecisionResult
                    {
                        Action = existing!.ActionTaken,
                        Reason = existing.DecisionReason,
                        Intent = existing.Intent,
                        Sentiment = existing.Sentiment,
                        IsSpam = existing.IsSpam,
                        SpamSeverity = existing.SpamSeverity
                    };
                }
                else
                {
                    // -- Step 1: Spam Detection -----------------------------
                    var spamResult = spamDetector.Analyze(evt.Message);

                    _logger.LogInformation(
                        "[SPAM] Result. EventId={EventId} IsSpam={IsSpam} Severity={Severity} Reason={Reason}",
                        evt.EventId,
                        spamResult.IsSpam,
                        spamResult.Severity,
                        spamResult.Reason ?? "-");

                    // -- Step 2: AI Analysis (ch? g?i n?u chua ch?c là spam) -
                    AiAnalysisResult aiResult;
                    if (spamResult.Severity == SpamSeverity.Harmful)
                    {
                        aiResult = AiAnalysisResult.Unknown;
                    }
                    else
                    {
                        aiResult = await aiAnalyzer.AnalyzeAsync(evt.Message, ct);
                    }

                    _logger.LogInformation(
                        "[AI] Result. EventId={EventId} Intent={Intent} Sentiment={Sentiment} Confidence={Confidence:0.00}",
                        evt.EventId,
                        aiResult.Intent,
                        aiResult.Sentiment,
                        aiResult.Confidence);

                    // -- Step 3: Decision -----------------------------------
                    decision = await decisionEngine.DecideAsync(
                        evt, spamResult, aiResult, ct);
                }

                _logger.LogInformation(
                    "[DECISION] EventId={EventId} Action={Action} Reason=\"{Reason}\"",
                    evt.EventId,
                    decision.Action,
                    decision.Reason ?? "-");

                // C?p nh?t state v?i k?t qu? phân tích
                state.IsSpam = decision.IsSpam;
                state.SpamSeverity = decision.SpamSeverity;
                state.Intent = decision.Intent;
                state.Sentiment = decision.Sentiment;
                state.ActionTaken = decision.Action;
                state.DecisionReason = decision.Reason;

                // -- Step 4: Execute Action ---------------------------------
                await actionExecutor.ExecuteAsync(evt, decision.Action, ct);

                // -- Hoàn thành ---------------------------------------------
                state.Status = ProcessingStatus.Processed;
                state.ProcessedAt = DateTimeOffset.UtcNow;
                state.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "[EVENT] Processed. Worker={WorkerId} EventId={EventId} Action={Action} Status={Status}",
                    workerId, evt.EventId, decision.Action, state.Status);

                // -- Commit offset Kafka ------------------------------------
                CommitOffset(kafkaResult);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown graceful — không commit, event s? du?c reprocess
                _logger.LogWarning(
                    "Processing cancelled for EventId={EventId} — will reprocess.",
                    evt.EventId);

                state.Status = ProcessingStatus.Received; // reset v? Received
                state.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(CancellationToken.None); // dùng None vì CT dã cancel
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[EVENT] Failed. Worker={WorkerId} EventId={EventId} Attempt={Attempt} Error=\"{Error}\"",
                    workerId, evt.EventId, state.RetryCount + 1, ex.Message);

                state.Status = ProcessingStatus.Failed;
                state.ErrorMessage = ex.Message;
                state.RetryCount++;
                state.UpdatedAt = DateTimeOffset.UtcNow;

                try
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx,
                        "Failed to save error state for EventId={EventId}.", evt.EventId);
                }

                // Commit malformed or internally failed events so one bad event does not
                // block the raw_events partition. Facebook action retries are handled by
                // BackendApi after commands are published.
                CommitOffset(kafkaResult);
            }
        }

        // -- Commit helpers ----------------------------------------------------
        // Worker ch? báo offset dã xong. Kafka thread gom offset liên t?c r?i commit th?t.

        private void CommitOffset(ConsumeResult<string, string> result)
        {
            if (!_commitChannel.Writer.TryWrite(result.TopicPartitionOffset))
            {
                _logger.LogError(
                    "Failed to queue offset commit for {TopicPartitionOffset}.",
                    result.TopicPartitionOffset);
            }
        }

        private void DrainCommits(
            IConsumer<string, string> consumer,
            Dictionary<TopicPartition, PartitionCommitState> states)
        {
            var offsetsToCommit = new List<TopicPartitionOffset>();

            while (_commitChannel.Reader.TryRead(out var completed))
            {
                var state = EnsureCommitState(
                    states,
                    completed.TopicPartition,
                    completed.Offset.Value);

                state.CompletedOffsets.Add(completed.Offset.Value);

                while (state.CompletedOffsets.Remove(state.NextOffsetToCommit))
                {
                    state.NextOffsetToCommit++;
                }

                if (state.NextOffsetToCommit > state.LastCommittedOffset)
                {
                    state.LastCommittedOffset = state.NextOffsetToCommit;
                    offsetsToCommit.Add(new TopicPartitionOffset(
                        completed.TopicPartition,
                        new Offset(state.NextOffsetToCommit)));
                }
            }

            if (offsetsToCommit.Count == 0) return;

            try
            {
                consumer.Commit(offsetsToCommit);
                _logger.LogDebug(
                    "Committed {Count} Kafka offsets.", offsetsToCommit.Count);
            }
            catch (KafkaException ex)
            {
                _logger.LogError(ex, "Kafka offset commit failed.");
            }
        }

        private static PartitionCommitState EnsureCommitState(
            Dictionary<TopicPartition, PartitionCommitState> states,
            TopicPartition topicPartition,
            long firstSeenOffset)
        {
            if (!states.TryGetValue(topicPartition, out var state))
            {
                state = new PartitionCommitState(firstSeenOffset);
                states[topicPartition] = state;
            }

            return state;
        }

        // -- Config helpers ----------------------------------------------------

        private ConsumerConfig BuildConsumerConfig() => new()
        {
            BootstrapServers = _kafkaOpts.BootstrapServers,
            GroupId = _kafkaOpts.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,

            // Ð? th?i gian cho AI call (10s timeout × MaxRetries = ~30s) + buffer
            MaxPollIntervalMs = 120_000,    // 2 phút

            // Session timeout ph?i < MaxPollIntervalMs
            SessionTimeoutMs = 30_000,     // 30 giây

            // T?i uu throughput khi có burst
            FetchMinBytes = 1,
            FetchWaitMaxMs = 100,
        };

        private static string Summarize(string? value, int maxLength = 120)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";

            var compact = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return compact.Length <= maxLength
                ? compact
                : compact[..maxLength] + "...";
        }

        private static async Task EnqueueReviewAsync(
            CoreDbContext db,
            NormalizedFacebookEvent evt,
            string reason,
            CancellationToken ct)
        {
            var existingQueueItem = await db.ReviewQueueItems
                .FindAsync([evt.EventId], ct);

            if (existingQueueItem is not null) return;

            db.ReviewQueueItems.Add(new ReviewQueueItem
            {
                EventId = evt.EventId,
                CommentId = evt.CommentId ?? string.Empty,
                Reason = reason,
                QueuedAt = DateTimeOffset.UtcNow
            });
        }

        private bool IsConfigValid() =>
            !string.IsNullOrWhiteSpace(_kafkaOpts.BootstrapServers) &&
            !string.IsNullOrWhiteSpace(_kafkaOpts.Topic) &&
            !string.IsNullOrWhiteSpace(_kafkaOpts.CommandTopic) &&
            !string.IsNullOrWhiteSpace(_kafkaOpts.GroupId);
    }

    internal record ChannelItem(
        ConsumeResult<string, string> ConsumeResult,
        NormalizedFacebookEvent Event);

    internal sealed class PartitionCommitState
    {
        public PartitionCommitState(long firstSeenOffset)
        {
            NextOffsetToCommit = firstSeenOffset;
            LastCommittedOffset = firstSeenOffset;
        }

        public long NextOffsetToCommit { get; set; }
        public long LastCommittedOffset { get; set; }
        public SortedSet<long> CompletedOffsets { get; } = new();
    }
}
