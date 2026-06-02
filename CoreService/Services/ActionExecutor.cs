using CoreService.Data;
using CoreService.Models;

namespace CoreService.Services
{
    public class ActionExecutor
    {
        private const string PositiveReply =
            "Cam on ban da ung ho shop!";

        private const string NegativeReply =
            "Rat xin loi vi trai nghiem chua tot. Shop se kiem tra va ho tro ban ngay.";

        private readonly IFacebookActionCommandPublisher _publisher;
        private readonly CoreDbContext _db;
        private readonly ILogger<ActionExecutor> _logger;

        public ActionExecutor(IFacebookActionCommandPublisher publisher, CoreDbContext db,
            ILogger<ActionExecutor> logger)
        {
            _publisher = publisher; _db = db; _logger = logger;
        }

        public async Task ExecuteAsync(
            NormalizedFacebookEvent evt,
            DecisionAction action,
            CancellationToken ct)
        {
            switch (action)
            { 
                case DecisionAction.HideComment:
                    await PublishAsync(evt, action, null, ct);
                    break;

                case DecisionAction.BlacklistUser:
                    await AddToBlacklistAsync(evt, ct);
                    await PublishAsync(evt, DecisionAction.HideComment, null, ct);
                    _logger.LogWarning("Blacklisted user {ActorId}", evt.ActorId);
                    break;

                case DecisionAction.QueueForReview:
                    await EnqueueReviewAsync(evt, "harmful_or_scam", ct);
                    await PublishAsync(evt, DecisionAction.HideComment, null, ct);
                    break;

                case DecisionAction.BlockUser:
                    if (string.IsNullOrWhiteSpace(evt.PageId) ||
                        string.IsNullOrWhiteSpace(evt.ActorId))
                    {
                        _logger.LogWarning(
                            "Cannot block user because PageId or ActorId is missing. EventId={EventId}",
                            evt.EventId);
                        return;
                    }

                    await EnqueueReviewAsync(evt, "manual_block_required", ct);

                    await PublishAsync(evt, action, null, ct);
                    break;

                case DecisionAction.ReplyPositive:
                    await PublishAsync(evt, action, PositiveReply, ct);
                    break;

                case DecisionAction.ReplyNegative:
                    await PublishAsync(evt, action, NegativeReply, ct);
                    break;

                case DecisionAction.None:
                    _logger.LogDebug("No action for EventId={EventId}", evt.EventId);
                    break;
            }
        }

        private Task PublishAsync(
            NormalizedFacebookEvent evt,
            DecisionAction action,
            string? message,
            CancellationToken ct)
        {
            return _publisher.PublishAsync(new FacebookActionCommand
            {
                CommandId = $"{evt.EventId}:{action}",
                EventId = evt.EventId,
                Action = action,
                PageId = evt.PageId,
                CommentId = evt.CommentId,
                ActorId = evt.ActorId,
                Message = message
            }, ct);
        }

        private async Task AddToBlacklistAsync(
            NormalizedFacebookEvent evt, CancellationToken ct)
        {
            var user = await _db.BlacklistedUsers
                .FindAsync([evt.ActorId!, evt.PageId], ct);

            if (user is null)
            {
                _db.BlacklistedUsers.Add(new BlacklistedUser
                {
                    UserId = evt.ActorId!,
                    PageId = evt.PageId,
                    SpamCount = 1,
                    FirstSpamAt = DateTimeOffset.UtcNow,
                    LastSpamAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                user.SpamCount++;
                user.LastSpamAt = DateTimeOffset.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }

        private async Task EnqueueReviewAsync(
            NormalizedFacebookEvent evt,
            string reason,
            CancellationToken ct)
        {
            var existingQueueItem = await _db.ReviewQueueItems
                .FindAsync([evt.EventId], ct);

            if (existingQueueItem is not null) return;

            _db.ReviewQueueItems.Add(new ReviewQueueItem
            {
                EventId = evt.EventId,
                CommentId = evt.CommentId ?? "",
                Reason = reason,
                QueuedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
    }
}
