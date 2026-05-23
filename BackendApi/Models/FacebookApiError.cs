using System.Text.Json.Serialization;

namespace BackendApi.Models
{
    public sealed class FacebookApiErrorEnvelope
    {
        [JsonPropertyName("error")]
        public FacebookApiError? Error { get; set; }
    }

    public sealed class FacebookApiError
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("error_subcode")]
        public int? ErrorSubcode { get; set; }

        [JsonPropertyName("fbtrace_id")]
        public string? FbtraceId { get; set; }
    }
}

