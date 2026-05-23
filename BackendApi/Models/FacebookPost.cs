using System.Text.Json.Serialization;

namespace BackendApi.Models
{
    public class FacebookPost
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("created_time")]
        public DateTime? CreatedTime { get; set; }
    }

    public class FacebookResponse<T>
    {
        [JsonPropertyName("data")]
        public List<T> Data { get; set; } = new List<T>();
    }
}
