using CoreService.Models;
using System.Text.Json.Serialization;

namespace CoreService.Services
{
    public sealed class FbErrorEnvelope
    {
        [JsonPropertyName("error")]
        public FbError? Error { get; set; }
    }
}
