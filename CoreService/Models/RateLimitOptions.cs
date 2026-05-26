namespace CoreService.Models
{
    public class RateLimitOptions
    {
        public int MaxEventsPerWindow { get; set; } = 20;
        public int WindowSeconds { get; set; } = 60;
    }
}
