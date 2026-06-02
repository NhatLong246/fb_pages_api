using CoreService.Data;
using CoreService.Models;
using Microsoft.EntityFrameworkCore;

namespace CoreService.Services
{
    public class DecisionEngine
    {
        private const int BlacklistThreshold = 3;
        private const int BlockThreshold = 5;

        private readonly CoreDbContext _db;
        private readonly ILogger<DecisionEngine> _logger;

        public DecisionEngine(CoreDbContext db, ILogger<DecisionEngine> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<DecisionResult> DecideAsync(
            NormalizedFacebookEvent evt,
            SpamResult spam,
            AiAnalysisResult ai,
            CancellationToken ct = default)
        {
            if (!spam.IsSpam)
            {
                return DecideAutomation(evt, ai);
            }

            var actorId = evt.ActorId;
            var pageId = evt.PageId;

            if (!string.IsNullOrWhiteSpace(actorId))
            {
                var blacklisted = await _db.BlacklistedUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        user => user.UserId == actorId && user.PageId == pageId,
                        ct);

                if (blacklisted is not null)
                {
                    var totalViolations = await CountUserSpamAllTimeAsync(actorId, pageId, ct);

                    _logger.LogWarning(
                        "Blacklisted user detected. ActorId={ActorId} TotalViolations={Count}",
                        actorId,
                        totalViolations);

                    if (totalViolations >= BlockThreshold)
                    {
                        return DecisionResult.Create(
                            DecisionAction.BlockUser,
                            spam,
                            ai,
                            $"User exceeded block threshold ({totalViolations} violations)");
                    }

                    return DecisionResult.Create(
                        DecisionAction.HideComment,
                        spam,
                        ai,
                        "User is blacklisted - hide without reply");
                }
            }

            switch (spam.Severity)
            {
                case SpamSeverity.Harmful:
                    _logger.LogWarning(
                        "Harmful content detected. CommentId={CommentId} Reason={Reason}",
                        evt.CommentId,
                        spam.Reason);

                    return DecisionResult.Create(
                        DecisionAction.QueueForReview,
                        spam,
                        ai,
                        $"Harmful content: {spam.Reason}");

                case SpamSeverity.Light:
                    if (string.IsNullOrWhiteSpace(actorId))
                    {
                        return DecisionResult.Create(
                            DecisionAction.HideComment,
                            spam,
                            ai,
                            "Light spam - unknown actor");
                    }

                    var spamCount24h = await CountUserSpamLast24hAsync(actorId, pageId, ct);

                    _logger.LogInformation(
                        "Light spam. ActorId={ActorId} SpamCount24h={Count}",
                        actorId,
                        spamCount24h + 1);

                    if (spamCount24h + 1 >= BlacklistThreshold)
                    {
                        return DecisionResult.Create(
                            DecisionAction.BlacklistUser,
                            spam,
                            ai,
                            $"Repeated spam {spamCount24h + 1} times in 24h - blacklist");
                    }

                    return DecisionResult.Create(
                        DecisionAction.HideComment,
                        spam,
                        ai,
                        $"Light spam - occurrence {spamCount24h + 1}/{BlacklistThreshold}");

                default:
                    return DecisionResult.Create(
                        DecisionAction.HideComment,
                        spam,
                        ai,
                        "Unknown spam severity - default hide");
            }
        }

        private static DecisionResult DecideAutomation(
            NormalizedFacebookEvent evt,
            AiAnalysisResult ai)
        {
            if (string.Equals(evt.ActorId, evt.PageId, StringComparison.Ordinal))
            {
                return DecisionResult.NoAction(ai, "Page-authored comment - skip auto reply");
            }

            return ai.Sentiment switch
            {
                "positive" => DecisionResult.Automation(
                    DecisionAction.ReplyPositive,
                    ai,
                    "Positive sentiment - thank the user"),
                "negative" => DecisionResult.Automation(
                    DecisionAction.ReplyNegative,
                    ai,
                    "Negative sentiment - apologize and offer support"),
                _ => DecisionResult.NoAction(ai, "Neutral sentiment - no automation")
            };
        }

        private Task<int> CountUserSpamLast24hAsync(
            string actorId,
            string pageId,
            CancellationToken ct)
        {
            var since = DateTimeOffset.UtcNow.AddHours(-24);

            return _db.EventStates
                .AsNoTracking()
                .CountAsync(state =>
                    state.ActorId == actorId &&
                    state.PageId == pageId &&
                    state.IsSpam &&
                    state.ReceivedAt >= since,
                    ct);
        }

        private Task<int> CountUserSpamAllTimeAsync(
            string actorId,
            string pageId,
            CancellationToken ct)
        {
            return _db.EventStates
                .AsNoTracking()
                .CountAsync(state =>
                    state.ActorId == actorId &&
                    state.PageId == pageId &&
                    state.IsSpam,
                    ct);
        }
    }
}
