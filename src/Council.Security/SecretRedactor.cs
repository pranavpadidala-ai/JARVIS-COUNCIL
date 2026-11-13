using System.Text.RegularExpressions;

namespace Council.Security;

/// <summary>
/// Best-effort redaction for accidental credential disclosure in persisted text.
/// This is a safety net, not a substitute for keeping secrets out of prompts and logs.
/// </summary>
public static class SecretRedactor
{
    private static readonly Regex AssignedSecret = new(
        @"(?<prefix>\b(?:api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|passwd|secret)\b\s*[:=]\s*)(?<value>""[^""]*""|'[^']*'|[^\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BearerCredential = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KnownToken = new(
        @"\b(?:AIza[0-9A-Za-z_-]{20,}|sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,})\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var redacted = AssignedSecret.Replace(text, match =>
            match.Groups["prefix"].Value + "\"[REDACTED]\"");
        redacted = BearerCredential.Replace(redacted, "Bearer [REDACTED]");
        return KnownToken.Replace(redacted, "[REDACTED]");
    }
}
