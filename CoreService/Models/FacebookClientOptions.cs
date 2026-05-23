namespace CoreService.Models
{
    public class FacebookClientOptions
    {
        public string PageAccessToken { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://graph.facebook.com/v19.0/";
    }
}
