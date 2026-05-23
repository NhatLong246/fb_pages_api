using CoreService.Models;
using System.Text.RegularExpressions;

namespace CoreService.Services
{
    public class SpamDetector
    {
        // Regex detect link
        private static readonly Regex LinkRegex = new(
            @"https?://|www\.|\.com|\.net|bit\.ly|tinyurl",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Patterns scam/bot ph? bi?n
        private static readonly string[] HarmfulPatterns =
            ["cu?c", "casino", "nh?p vào dây", "mi?n phí 100%", "trúng thu?ng",
         "click here", "free money", "investment guaranteed"];

        public SpamResult Analyze(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return new SpamResult(false, SpamSeverity.None, "empty");

            // Check harmful/scam
            var lower = message.ToLowerInvariant();
            if (HarmfulPatterns.Any(p => lower.Contains(p)))
                return new SpamResult(true, SpamSeverity.Harmful, "harmful_content");

            // Check link
            if (LinkRegex.IsMatch(message))
                return new SpamResult(true, SpamSeverity.Light, "contains_link");

            // Check l?p n?i dung (message quá ng?n, toàn emoji, ho?c ch? 1-2 t? l?p l?i)
            var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var distinctRatio = words.Distinct().Count() / (double)Math.Max(words.Length, 1);
            if (words.Length >= 4 && distinctRatio < 0.4)
                return new SpamResult(true, SpamSeverity.Light, "repetitive_content");

            return new SpamResult(false, SpamSeverity.None, null);
        }
    }

    public record SpamResult(bool IsSpam, SpamSeverity Severity, string? Reason);
}
