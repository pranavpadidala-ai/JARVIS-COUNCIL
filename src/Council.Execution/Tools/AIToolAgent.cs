using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.Execution.Tools;

/// <summary>
/// Selects and invokes only tools explicitly supplied to this agent.
/// The model never receives unrestricted shell or filesystem access.
/// </summary>
public sealed class AIToolAgent
{
    private readonly IAIProvider _provider;
    private readonly IReadOnlyDictionary<string, ITool> _tools;

    public AIToolAgent(IAIProvider provider, IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(tools);

        _provider = provider;
        _tools = tools
            .Where(tool => tool is not null)
            .ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);

        if (_tools.Count == 0)
            throw new ArgumentException("At least one tool must be registered.", nameof(tools));
    }

    public async Task<ToolResult> ExecuteAsync(
        string userRequest,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userRequest))
            throw new ArgumentException("A tool request is required.", nameof(userRequest));

        var toolCatalog = string.Join(
            Environment.NewLine,
            _tools.Values.Select(tool => $"- {tool.Name}: {tool.Description}"));

        var prompt = $$"""
            You are JARVIS, a tool-selection planner.
            Choose exactly one tool from the allow-list below to fulfil the user's request.
            Return ONLY one JSON object in this exact shape:
            {"tool":"tool_name","arguments":{"argument_name":"string_value"}}
            Do not use markdown fences. Do not invent tools or arguments.
            If no listed tool is suitable, return:
            {"tool":"none","arguments":{}}

            Available tools:
            {{toolCatalog}}

            User request:
            {{userRequest}}
            """;

        var response = await _provider.AskAsync(
            new AIRequest([new AIMessage("user", prompt)], Temperature: 0),
            cancellationToken);

        using var document = JsonDocument.Parse(ExtractJson(response.Content));
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tool", out var toolElement) ||
            toolElement.ValueKind != JsonValueKind.String)
        {
            return new ToolResult(false, "The AI planner returned an invalid tool selection.");
        }

        var toolName = toolElement.GetString();
        if (string.IsNullOrWhiteSpace(toolName) ||
            string.Equals(toolName, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolResult(false, "No registered tool can safely fulfil this request.");
        }

        if (!_tools.TryGetValue(toolName, out var tool))
            return new ToolResult(false, $"The AI selected an unregistered tool: {toolName}");

        if (!root.TryGetProperty("arguments", out var argumentsElement) ||
            argumentsElement.ValueKind != JsonValueKind.Object)
        {
            return new ToolResult(false, "The AI planner did not provide a valid arguments object.");
        }

        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in argumentsElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                return new ToolResult(false, $"Tool argument '{property.Name}' must be a string.");

            arguments[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await tool.ExecuteAsync(arguments, cancellationToken);
    }

    private static string ExtractJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("The AI planner returned an empty response.");

        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
        }

        return trimmed;
    }
}
