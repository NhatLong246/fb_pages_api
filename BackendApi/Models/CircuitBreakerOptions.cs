namespace BackendApi.Models
{
    public class CircuitBreakerOptions
    {
        public int FailureThreshold { get; set; } = 10;
        public int BreakSeconds { get; set; } = 60;
    }
}
