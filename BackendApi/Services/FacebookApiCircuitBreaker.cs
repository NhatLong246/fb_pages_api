using BackendApi.Models;
using Microsoft.Extensions.Options;

namespace BackendApi.Services
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
                if (DateTimeOffset.UtcNow < _openedUntil.Value)
                {
                    throw new HttpRequestException(
                        $"Facebook API circuit is open until {_openedUntil:O}");
                }

                _openedUntil = null;
                _logger.LogWarning("[CIRCUIT] Half-open. Allowing the next Facebook API request.");
            }
        }

        public void RecordSuccess()
        {
            lock (_gate)
            {
                _failureCount = 0;
                _openedUntil = null;
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
                        "[CIRCUIT] Failure recorded. Count={Count}/{Threshold} Error=\"{Error}\"",
                        _failureCount,
                        _options.FailureThreshold,
                        ex.Message);
                    return;
                }

                _openedUntil = DateTimeOffset.UtcNow.AddSeconds(_options.BreakSeconds);
                _logger.LogError(
                    "[CIRCUIT] Opened for {BreakSeconds}s after {Count} consecutive failures.",
                    _options.BreakSeconds,
                    _failureCount);
            }
        }
    }
}
