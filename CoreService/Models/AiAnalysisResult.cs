using System.Text.Json.Serialization;

namespace CoreService.Models
{
    public class AiAnalysisResult
    {
        [JsonPropertyName("intent")]
        public string Intent { get; set; } = "other";

        [JsonPropertyName("sentiment")]
        public string Sentiment { get; set; } = "neutral";

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; } = 0.0;

        /// <summary>Fallback khi AI không kh? d?ng.</summary>
        public static AiAnalysisResult Unknown => new()
        {
            Intent = "other",
            Sentiment = "neutral",
            Confidence = 0.0
        };

        /// <summary>Ð? tin c?y d? cao d? dùng k?t qu? AI.</summary>
        public bool IsReliable => Confidence >= 0.6;
    }

    // -------------------------------------------------------------------------
    // Custom exception d? phân bi?t l?i parse vs l?i m?ng
    // -------------------------------------------------------------------------
    public class AiParseException : Exception
    {
        public AiParseException(string message) : base(message) { }
        public AiParseException(string message, Exception inner) : base(message, inner) { }
    }
}
