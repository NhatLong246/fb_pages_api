namespace WebhookService.Models;

public class FacebookWebhookOptions
{
    public string VerifyToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;
    public string PageId { get; set; } = string.Empty;
    public string PageAccessToken { get; set; } = string.Empty;
}
