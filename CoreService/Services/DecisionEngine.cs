using CoreService.Data;
using CoreService.Models;
using Microsoft.EntityFrameworkCore;

namespace CoreService.Services
{
    public class DecisionEngine
    {
        // Ngu?ng spam l?p l?i trong 24h d? blacklist
        private const int BlacklistThreshold = 3;

        // Ngu?ng vi ph?m tích luy d? d? xu?t block h?n trên Facebook
        private const int BlockThreshold = 5;

        private readonly CoreDbContext _db;
        private readonly ILogger<DecisionEngine> _logger;

        public DecisionEngine(CoreDbContext db, ILogger<DecisionEngine> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Quy?t d?nh action d?a trên k?t qu? spam + AI.
        /// Uu tiên: Harmful > Blacklisted user > Repeated spam > Light spam > None
        /// </summary>
        public async Task<DecisionResult> DecideAsync(
            NormalizedFacebookEvent evt,
            SpamResult spam,
            AiAnalysisResult ai,
            CancellationToken ct = default)
        {
            // -- 1. Không spam ? không làm gì --------------------------------
            if (!spam.IsSpam)
            {
                return DecisionResult.NoAction(ai);
            }

            var actorId = evt.ActorId;
            var pageId = evt.PageId;

            // -- 2. Ki?m tra user dã trong blacklist chua --------------------
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                var blacklisted = await _db.BlacklistedUsers
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        u => u.UserId == actorId && u.PageId == pageId, ct);

                if (blacklisted is not null)
                {
                    var totalViolations = await CountUserSpamAllTimeAsync(actorId, pageId, ct);

                    _logger.LogWarning(
                        "Blacklisted user detected. ActorId={ActorId} TotalViolations={Count}",
                        actorId, totalViolations);

                    // Vu?t ngu?ng block ? d? xu?t block h?n trên Facebook Page
                    if (totalViolations >= BlockThreshold)
                    {
                        return DecisionResult.Create(
                            DecisionAction.BlockUser, spam, ai,
                            $"User exceeded block threshold ({totalViolations} violations)");
                    }

                    // V?n trong blacklist nhung chua d?n ngu?ng block
                    // ? hide comment, không auto reply
                    return DecisionResult.Create(
                        DecisionAction.HideComment, spam, ai,
                        "User is blacklisted — hide without reply");
                }
            }

            // -- 3. Phân lo?i theo m?c d? spam --------------------------------
            switch (spam.Severity)
            {
                // N?i dung d?c h?i / scam / bot rõ ràng
                case SpamSeverity.Harmful:
                    _logger.LogWarning(
                        "Harmful content detected. CommentId={CommentId} Reason={Reason}",
                        evt.CommentId, spam.Reason);

                    return DecisionResult.Create(
                        DecisionAction.QueueForReview, spam, ai,
                        $"Harmful content: {spam.Reason}");

                // Spam nh? ? ki?m tra t?n su?t trong 24h
                case SpamSeverity.Light:
                    if (string.IsNullOrWhiteSpace(actorId))
                    {
                        // Không có actorId ? ch? hide, không th? track user
                        return DecisionResult.Create(
                            DecisionAction.HideComment, spam, ai,
                            "Light spam — unknown actor");
                    }

                    var spamCount24h = await CountUserSpamLast24hAsync(actorId, pageId, ct);

                    _logger.LogInformation(
                        "Light spam. ActorId={ActorId} SpamCount24h={Count}",
                        actorId, spamCount24h + 1); // +1 vì event hi?n t?i chua du?c luu

                    // L?n này là l?n th? >= BlacklistThreshold ? blacklist
                    // (+1 vì event hi?n t?i chua du?c commit vào DB)
                    if (spamCount24h + 1 >= BlacklistThreshold)
                    {
                        return DecisionResult.Create(
                            DecisionAction.BlacklistUser, spam, ai,
                            $"Repeated spam {spamCount24h + 1} times in 24h ? blacklist");
                    }

                    // Spam nh?, chua d? ngu?ng ? hide bình lu?n
                    return DecisionResult.Create(
                        DecisionAction.HideComment, spam, ai,
                        $"Light spam — occurrence {spamCount24h + 1}/{BlacklistThreshold}");

                default:
                    return DecisionResult.Create(
                        DecisionAction.HideComment, spam, ai,
                        "Unknown spam severity — default hide");
            }
        }

        // -- Private helpers --------------------------------------------------

        /// <summary>Ð?m s? event spam c?a user trong vòng 24 gi? qua.</summary>
        private Task<int> CountUserSpamLast24hAsync(
            string actorId, string pageId, CancellationToken ct)
        {
            var since = DateTimeOffset.UtcNow.AddHours(-24);

            return _db.EventStates
                .AsNoTracking()
                .CountAsync(e =>
                    e.ActorId == actorId &&
                    e.PageId == pageId &&
                    e.IsSpam == true &&
                    e.ReceivedAt >= since,
                    ct);
        }

        /// <summary>Ð?m t?ng s? vi ph?m c?a user t? tru?c t?i nay.</summary>
        private Task<int> CountUserSpamAllTimeAsync(
            string actorId, string pageId, CancellationToken ct)
        {
            return _db.EventStates
                .AsNoTracking()
                .CountAsync(e =>
                    e.ActorId == actorId &&
                    e.PageId == pageId &&
                    e.IsSpam == true,
                    ct);
        }
    }
}
