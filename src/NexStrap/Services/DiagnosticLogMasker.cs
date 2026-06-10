using System.Text.RegularExpressions;

namespace NexStrap.Services;

public static partial class DiagnosticLogMasker
{
    public static string Mask(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        text = UserPathRegex().Replace(text, @"C:\Users<user>");
        text = AuthKeyValueRegex().Replace(text, m => $"{m.Groups[1].Value}=[REDACTED]");
        text = EmailRegex().Replace(text, "[REDACTED EMAIL]");

        // Generic long-token rule must run last, after the more specific rules above.
        text = LongTokenRegex().Replace(text, "[REDACTED]");

        return text;
    }

    [GeneratedRegex(@"C:\\Users\\[^\\/\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex UserPathRegex();

    [GeneratedRegex(@"(\.ROBLOSECURITY|ROBLOSECURITY|authenticationTicket|cookie|token)\s*[:=]\s*\S+",
        RegexOptions.IgnoreCase)]
    private static partial Regex AuthKeyValueRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"[A-Za-z0-9+/=_\-\.]{40,}")]
    private static partial Regex LongTokenRegex();
}
