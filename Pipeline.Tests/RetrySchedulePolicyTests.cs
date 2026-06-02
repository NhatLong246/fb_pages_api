using RetryService.Services;
using Xunit;

namespace Pipeline.Tests;

public class RetrySchedulePolicyTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    public void GetDelaySeconds_returns_exponential_schedule(int retryCount, int expectedSeconds)
    {
        Assert.Equal(expectedSeconds, RetrySchedulePolicy.GetDelaySeconds(retryCount));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void IsExhausted_stops_after_three_retries(int retryCount, bool expected)
    {
        Assert.Equal(expected, RetrySchedulePolicy.IsExhausted(retryCount, 3));
    }
}
