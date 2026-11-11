using Council.Execution.Tools;

namespace Council.App.Abaqus;

/// <summary>
/// Read-only diagnostic tool. It only locates the Abaqus launcher; it never starts a job or edits a model.
/// </summary>
public sealed class AbaqusStatusTool : ITool
{
    private readonly AbaqusInstallationDetector _detector;

    public AbaqusStatusTool(AbaqusInstallationDetector? detector = null)
    {
        _detector = detector ?? new AbaqusInstallationDetector();
    }

    public string Name => "abaqus_status";

    public string Description =>
        "Read-only check for the Abaqus command launcher. Takes no arguments, does not open Abaqus, run jobs, or modify models.";

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (arguments.Count != 0)
        {
            return Task.FromResult(new ToolResult(
                false,
                "This diagnostic accepts no arguments."));
        }

        var installation = _detector.Detect();
        var message = installation.IsAvailable
            ? installation.Message + Environment.NewLine +
              "Launcher: " + installation.CommandPath
            : installation.Message;

        return Task.FromResult(new ToolResult(true, message));
    }
}
