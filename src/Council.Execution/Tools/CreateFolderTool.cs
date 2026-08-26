namespace Council.Execution.Tools;

public sealed class CreateFolderTool : ITool
{
    public string Name => "create_folder";

    public string Description =>
        "Creates a folder at a specified path.";

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("path", out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(
                new ToolResult(
                    false,
                    "A folder path is required."));
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (Directory.Exists(fullPath))
            {
                return Task.FromResult(
                    new ToolResult(
                        true,
                        $"Folder already exists: {fullPath}"));
            }

            Directory.CreateDirectory(fullPath);

            return Task.FromResult(
                new ToolResult(
                    true,
                    $"Folder created successfully: {fullPath}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                new ToolResult(
                    false,
                    $"Unable to create folder: {ex.Message}"));
        }
    }
}
