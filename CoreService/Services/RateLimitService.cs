using CoreService.Data;
using CoreService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CoreService.Services
{
    public class RateLimitService
    {
        private readonly CoreDbContext _db;
        private readonly RateLimitOptions _options;

        public RateLimitService(
            CoreDbContext db,
            IOptions<RateLimitOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public async Task<RateLimitResult> CheckAsync(
            NormalizedFacebookEvent evt,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(evt.ActorId) ||
                string.IsNullOrWhiteSpace(evt.PageId))
            {
                return RateLimitResult.NotLimited(0, _options.MaxEventsPerWindow);
            }

            var since = DateTimeOffset.UtcNow.AddSeconds(-_options.WindowSeconds);
            var recentCount = await _db.EventStates
                .AsNoTracking()
                .CountAsync(e =>
                    e.ActorId == evt.ActorId &&
                    e.PageId == evt.PageId &&
                    e.ReceivedAt >= since,
                    ct);

            var projectedCount = recentCount + 1;
            return projectedCount >= _options.MaxEventsPerWindow
                ? RateLimitResult.Limited(
                    projectedCount,
                    _options.MaxEventsPerWindow,
                    _options.WindowSeconds)
                : RateLimitResult.NotLimited(projectedCount, _options.MaxEventsPerWindow);
        }
    }

    public record RateLimitResult(
        bool IsLimited,
        int CurrentCount,
        int Limit,
        int WindowSeconds,
        string? Reason)
    {
        public static RateLimitResult NotLimited(int currentCount, int limit)
            => new(false, currentCount, limit, 0, null);

        public static RateLimitResult Limited(
            int currentCount,
            int limit,
            int windowSeconds)
            => new(
                true,
                currentCount,
                limit,
                windowSeconds,
                $"Rate limit exceeded: {currentCount}/{limit} events in {windowSeconds}s");
    }
}
