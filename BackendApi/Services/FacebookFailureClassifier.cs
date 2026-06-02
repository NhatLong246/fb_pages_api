using System.Net;

namespace BackendApi.Services
{
    public static class FacebookFailureClassifier
    {
        public static bool IsRetryable(HttpStatusCode statusCode) =>
            statusCode == HttpStatusCode.RequestTimeout ||
            statusCode == HttpStatusCode.TooManyRequests ||
            (int)statusCode >= 500;
    }
}
