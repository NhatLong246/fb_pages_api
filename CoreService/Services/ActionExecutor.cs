using CoreService.Data;
using CoreService.Models;

namespace CoreService.Services
{
    public class ActionExecutor
    {
        private readonly IFacebookApiClient _fb;
        private readonly CoreDbContext _db;
        private readonly ILogger<ActionExecutor> _logger;

        public ActionExecutor(IFacebookApiClient fb, CoreDbContext db,
            ILogger<ActionExecutor> logger)
        {
            _fb = fb; _db = db; _logger = logger;
        }

        public async Task ExecuteAsync(
            NormalizedFacebookEvent evt,
            DecisionAction action,
            CancellationToken ct)
        {
            switch (action)
            { 
                case DecisionAction.HideComment:
                    await _fb.HideCommentAsync(evt.CommentId!, ct);
                    _logger.LogInformation("Hidden comment {CommentId}", evt.CommentId);
                    break;

                case DecisionAction.BlacklistUser:
                    await AddToBlacklistAsync(evt, ct);
                    await _fb.HideCommentAsync(evt.CommentId!, ct);
                    _logger.LogWarning("Blacklisted user {ActorId}", evt.ActorId);
                    break;

                case DecisionAction.QueueForReview:
                    await EnqueueReviewAsync(evt, "harmful_or_scam", ct);

                    await _fb.HideCommentAsync(evt.CommentId!, ct);
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

                    try
                    {
                        await _fb.BlockUserAsync(evt.PageId, evt.ActorId, ct);
                        await MarkBlockedAsync(evt, ct);
                        _logger.LogWarning(
                            "Blocked user {ActorId} from page {PageId}",
                            evt.ActorId,
                            evt.PageId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "BlockUser API failed for ActorId={ActorId}. Queued for manual block.",
                            evt.ActorId);
                    }
                    break;

                case DecisionAction.None:
                    _logger.LogDebug("No action for EventId={EventId}", evt.EventId);
                    break;
            }
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

        private async Task MarkBlockedAsync(
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
                    LastSpamAt = DateTimeOffset.UtcNow,
                    IsBlocked = true
                });
            }
            else
            {
                user.IsBlocked = true;
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
