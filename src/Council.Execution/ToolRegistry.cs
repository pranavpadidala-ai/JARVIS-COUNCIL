using Council.Execution.Tools;

namespace Council.Execution;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    public void Register(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new ArgumentException("Tool name cannot be empty.", nameof(tool));

        _tools[tool.Name] = tool;
    }

    public bool TryGet(string name, out ITool tool)
    {
        return _tools.TryGetValue(name, out tool!);
    }

    public IReadOnlyCollection<ITool> GetAll()
    {
        return _tools.Values.ToArray();
    }
}
