using System.Net;
using BackendApi.Services;
using Xunit;

namespace Pipeline.Tests;

public class FacebookFailureClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    public void IsRetryable_only_retries_transient_statuses(HttpStatusCode statusCode, bool expected)
    {
        Assert.Equal(expected, FacebookFailureClassifier.IsRetryable(statusCode));
    }
}
