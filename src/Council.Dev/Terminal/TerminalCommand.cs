namespace Council.Dev.Terminal;

public sealed record TerminalCommand(
    string Command,
    string WorkingDirectory,
    TimeSpan Timeout);
