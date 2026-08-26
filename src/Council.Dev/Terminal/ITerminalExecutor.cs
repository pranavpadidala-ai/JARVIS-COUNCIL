namespace Council.Dev.Terminal;

public interface ITerminalExecutor
{
    Task<TerminalResult> ExecuteAsync(
        TerminalCommand command,
        CancellationToken cancellationToken = default);
}
