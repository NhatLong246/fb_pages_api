using System.Text.Json.Serialization;

namespace CoreService.Models
{
    public sealed class FbError
    {
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("error_subcode")] public int? ErrorSubcode { get; set; }
        [JsonPropertyName("fbtrace_id")] public string? FbtraceId { get; set; }
    }
}
