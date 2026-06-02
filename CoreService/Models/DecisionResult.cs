using CoreService.Services;

namespace CoreService.Models
{
    public class DecisionResult
    {
        public DecisionAction Action { get; init; }
        public string? Reason { get; init; }
        public string? Intent { get; init; }
        public string? Sentiment { get; init; }
        public bool IsSpam { get; init; }
        public SpamSeverity SpamSeverity { get; init; }

        public static DecisionResult NoAction(
            AiAnalysisResult ai,
            string? reason = null) => new()
        {
            Action = DecisionAction.None,
            Reason = reason,
            Intent = ai.Intent,
            Sentiment = ai.Sentiment,
            IsSpam = false,
            SpamSeverity = SpamSeverity.None
        };

        public static DecisionResult Automation(
            DecisionAction action,
            AiAnalysisResult ai,
            string reason) => new()
        {
            Action = action,
            Reason = reason,
            Intent = ai.Intent,
            Sentiment = ai.Sentiment,
            IsSpam = false,
            SpamSeverity = SpamSeverity.None
        };

        public static DecisionResult Create(
            DecisionAction action,
            SpamResult spam,
            AiAnalysisResult ai,
            string reason) => new()
            {
                Action = action,
                Reason = reason,
                Intent = ai.Intent,
                Sentiment = ai.Sentiment,
                IsSpam = spam.IsSpam,
                SpamSeverity = spam.Severity
            };
    }
}
