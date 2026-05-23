namespace BackendApi.Models
{
    public class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }

    public class SuccessResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
