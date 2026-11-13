using System.Text;
using System.Text.RegularExpressions;
using Council.Dev.Terminal;

namespace Council.Dev.Validation;

public sealed class ValidationLogStore
{
    private static readonly Regex SensitiveAssignment = new(
        @"(?i)\b(api[_-]?key|access[_-]?token|refresh[_-]?token|password|client[_-]?secret)\b\s*[:=]\s*([^\s,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BearerToken = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly string _root;

    public ValidationLogStore(string? rootDirectory = null)
    {
        _root = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JARVIS", "validation-logs"));
    }

    public async Task<string> SaveAsync(string runId, string stage, TerminalResult result,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Invalid validation run ID.", nameof(runId));
        if (stage is not ("build" or "test"))
            throw new ArgumentException("Validation stage must be build or test.", nameof(stage));

        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, runId + "-" + stage + ".log");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var raw = new StringBuilder()
            .AppendLine("Stage: " + stage)
            .AppendLine("Success: " + result.Success)
            .AppendLine("Exit code: " + result.ExitCode)
            .AppendLine("STDOUT:")
            .AppendLine(result.StandardOutput ?? string.Empty)
            .AppendLine("STDERR:")
            .AppendLine(result.StandardError ?? string.Empty)
            .ToString();
        var safe = BearerToken.Replace(SensitiveAssignment.Replace(raw, "$1=[REDACTED]"), "Bearer [REDACTED]");
        try
        {
            await File.WriteAllTextAsync(temporary, safe, cancellationToken);
            File.Move(temporary, path, true);
            return path;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}