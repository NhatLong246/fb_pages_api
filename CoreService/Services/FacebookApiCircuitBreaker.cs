using CoreService.Models;
using Microsoft.Extensions.Options;

namespace CoreService.Services
{
    public class FacebookApiCircuitBreaker
    {
        private readonly CircuitBreakerOptions _options;
        private readonly ILogger<FacebookApiCircuitBreaker> _logger;
        private readonly object _gate = new();
        private int _failureCount;
        private DateTimeOffset? _openedUntil;

        public FacebookApiCircuitBreaker(
            IOptions<CircuitBreakerOptions> options,
            ILogger<FacebookApiCircuitBreaker> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public void ThrowIfOpen()
        {
            lock (_gate)
            {
                if (_openedUntil is null) return;

                var now = DateTimeOffset.UtcNow;
                if (now < _openedUntil.Value)
                {
                    throw new FacebookApiCircuitOpenException(_openedUntil.Value);
                }

                _openedUntil = null;
                _logger.LogWarning("[CIRCUIT] Facebook API circuit half-open. Next request is allowed.");
            }
        }

        public void RecordSuccess()
        {
            lock (_gate)
            {
                if (_failureCount == 0 && _openedUntil is null) return;

                _failureCount = 0;
                _openedUntil = null;
                _logger.LogInformation("[CIRCUIT] Facebook API circuit closed after successful request.");
            }
        }

        public void RecordFailure(Exception ex)
        {
            lock (_gate)
            {
                _failureCount++;

                if (_failureCount < _options.FailureThreshold)
                {
                    _logger.LogWarning(
                        "[CIRCUIT] Facebook API failure recorded. Count={Count}/{Threshold} Error=\"{Error}\"",
                        _failureCount,
                        _options.FailureThreshold,
                        ex.Message);
                    return;
                }

                _openedUntil = DateTimeOffset.UtcNow.AddSeconds(_options.BreakSeconds);
                _logger.LogError(
                    "[CIRCUIT] Facebook API circuit opened for {BreakSeconds}s after {Count} consecutive failures.",
                    _options.BreakSeconds,
                    _failureCount);
            }
        }
    }

    public sealed class FacebookApiCircuitOpenException : Exception
    {
        public DateTimeOffset OpenedUntil { get; }

        public FacebookApiCircuitOpenException(DateTimeOffset openedUntil)
            : base($"Facebook API circuit is open until {openedUntil:O}")
        {
            OpenedUntil = openedUntil;
        }
    }
}
