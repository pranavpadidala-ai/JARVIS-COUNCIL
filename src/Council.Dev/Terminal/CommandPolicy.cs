namespace Council.Dev.Terminal;

public sealed class CommandPolicy
{
    private static readonly HashSet<string> AllowedCommands =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "dotnet",
            "git"
        };

    private static readonly string[] BlockedOperators =
    [
        ";",
        "|",
        "||",
        "&&",
        ">",
        ">>",
        "<",
        "`",
        "$(",
        "&"
    ];

    private static readonly string[] BlockedPowerShellCommands =
    [
        "powershell",
        "pwsh",
        "cmd",
        "cmd.exe",
        "invoke-expression",
        "iex",
        "invoke-command",
        "start-process",
        "stop-process",
        "remove-item",
        "set-content",
        "add-content",
        "invoke-webrequest",
        "invoke-restmethod",
        "set-executionpolicy"
    ];

    public bool IsAllowed(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        // PowerShell treats line breaks as statement separators. Reject
        // all control characters before applying the command allowlist.
        if (command.Any(char.IsControl))
        {
            return false;
        }

        var trimmed = command.Trim();

        if (ContainsBlockedOperator(trimmed))
        {
            return false;
        }

        if (ContainsBlockedPowerShellCommand(trimmed))
        {
            return false;
        }

        var executable = ExtractExecutable(trimmed);

        return AllowedCommands.Contains(executable);
    }

    private static bool ContainsBlockedOperator(string command)
    {
        foreach (var blockedOperator in BlockedOperators)
        {
            if (command.Contains(
                blockedOperator,
                StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsBlockedPowerShellCommand(string command)
    {
        var lower = command.ToLowerInvariant();

        foreach (var blockedCommand in BlockedPowerShellCommands)
        {
            if (lower.Contains(blockedCommand))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractExecutable(string command)
    {
        var trimmed = command.Trim();

        var firstToken = trimmed
            .Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstToken))
        {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(firstToken);
    }
}
