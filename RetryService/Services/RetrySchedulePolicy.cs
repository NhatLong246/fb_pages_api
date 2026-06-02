namespace RetryService.Services
{
    public static class RetrySchedulePolicy
    {
        public static int GetDelaySeconds(int retryCount) =>
            (int)Math.Pow(2, Math.Max(retryCount, 0));

        public static bool IsExhausted(int retryCount, int maxRetryAttempts) =>
            retryCount >= maxRetryAttempts;
    }
}
