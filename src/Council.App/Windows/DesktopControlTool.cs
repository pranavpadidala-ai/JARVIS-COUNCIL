using System.Diagnostics;
using Council.Execution.Tools;

namespace Council.App.Windows;

/// <summary>
/// Minimal Windows desktop bridge. It can launch only a small, fixed set of
/// harmless built-in applications; it cannot run shell commands or arbitrary paths.
/// </summary>
public sealed class DesktopControlTool : ITool
{
    private static readonly IReadOnlyDictionary<string, string> AllowedApps =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["notepad"] = "notepad.exe",
            ["calculator"] = "calc.exe",
            ["file_explorer"] = "explorer.exe",
            ["task_manager"] = "taskmgr.exe"
        };

    private readonly Func<string, bool> _launch;

    public DesktopControlTool(Func<string, bool>? launch = null)
    {
        _launch = launch ?? LaunchProcess;
    }

    public string Name => "desktop_control";

    public string Description =>
        "Safe Windows desktop launcher. Required arguments: action=launch_app and app=notepad|calculator|file_explorer|task_manager. Only these built-in apps can be launched. No shell commands, arbitrary executable paths, scripts, or unlock actions are supported.";

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            return Task.FromResult(new ToolResult(false, "Desktop app launching is supported on Windows only."));

        if (arguments.Count != 2 ||
            !arguments.TryGetValue("action", out var action) ||
            !arguments.TryGetValue("app", out var app))
        {
            return Task.FromResult(new ToolResult(false, "Provide exactly action=launch_app and app=<allowlisted app>."));
        }

        if (!string.Equals(action, "launch_app", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(new ToolResult(false, "Only the launch_app action is supported."));

        if (!AllowedApps.TryGetValue(app.Trim(), out var executable))
            return Task.FromResult(new ToolResult(false,
                "App is not allowlisted. Choose notepad, calculator, file_explorer, or task_manager."));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_launch(executable)
                ? new ToolResult(true, "Launched " + app.Trim() + ".")
                : new ToolResult(false, "Windows did not confirm that the application started."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(new ToolResult(false, "Could not launch the application: " + ex.Message));
        }
    }

    private static bool LaunchProcess(string executable)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true
        });
        return process is not null;
    }
}
