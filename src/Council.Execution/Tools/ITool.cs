namespace Council.Execution.Tools;

public interface ITool
{
    string Name { get; }

    string Description { get; }

    Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default);
}

public sealed record ToolResult(
    bool Success,
    string Message);
