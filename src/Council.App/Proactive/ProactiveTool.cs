using Council.Execution.Tools;

namespace Council.App.Proactive;

/// <summary>Lets the user turn JARVIS's unprompted tray suggestions on or off.</summary>
public sealed class ProactiveTool : ITool
{
    private readonly ProactiveAdvisor _advisor;

    public ProactiveTool(ProactiveAdvisor? advisor = null) => _advisor = advisor ?? new ProactiveAdvisor();

    public string Name => "proactive_suggestions";

    public string Description =>
        "Turn JARVIS's proactive tray suggestions on or off, or check them. Arguments: action = on | off | status. " +
        "Suggestions: morning summary, evening habit check, water, low battery, high memory, late-night sleep nudge. " +
        "Use off when the user says the suggestions are annoying.";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var action = arguments.FirstOrDefault(a => a.Key.Equals("action", StringComparison.OrdinalIgnoreCase)).Value?.Trim().ToLowerInvariant();
        try
        {
            switch (action)
            {
                case "on": _advisor.SetEnabled(true); return Done(true, "Proactive suggestions are on.");
                case "off": _advisor.SetEnabled(false); return Done(true, "Proactive suggestions are off. Say 'turn suggestions on' to bring them back.");
                case "status": return Done(true, _advisor.Enabled ? "Proactive suggestions are on." : "Proactive suggestions are off.");
                default: return Done(false, "action must be on, off or status.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Done(false, ex.Message);
        }
    }

    private static Task<ToolResult> Done(bool ok, string message) => Task.FromResult(new ToolResult(ok, message));
}
