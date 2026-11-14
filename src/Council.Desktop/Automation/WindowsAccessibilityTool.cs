using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Council.Execution.Tools;

namespace Council.Desktop.Automation;

/// <summary>
/// Read-only UI Automation inspection of the current foreground window.
/// It reports control metadata only and never reads text/value patterns or invokes controls.
/// </summary>
public sealed class WindowsAccessibilityTool : ITool
{
    private const int MaximumControls = 120;

    public string Name => "windows_accessibility";

    public string Description =>
        "Read-only Windows UI Automation inspection. Only action=inspect is supported. " +
        "Inspects the currently foreground window and returns visible control names, control types, automation IDs, enabled state and screen bounds. " +
        "It never reads text-entry values, clicks, types, launches apps, or touches hidden/background windows. " +
        "Use this to identify controls before the separately available screenshot-verified computer_use actions.";

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
            return Task.FromResult(new ToolResult(false, "Windows UI Automation is available only on Windows."));

        if (arguments.Keys.Any(key => key is not ("action" or "query" or "max_items")) ||
            !arguments.TryGetValue("action", out var action) ||
            !string.Equals(action, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ToolResult(false,
                "Supported operation: action=inspect, with optional query and max_items (1-120)."));
        }

        var query = arguments.TryGetValue("query", out var suppliedQuery)
            ? suppliedQuery.Trim()
            : string.Empty;
        if (query.Length > 120)
            return Task.FromResult(new ToolResult(false, "query must not exceed 120 characters."));

        var maxItems = MaximumControls;
        if (arguments.TryGetValue("max_items", out var requestedMax) &&
            (!int.TryParse(requestedMax, out maxItems) || maxItems < 1 || maxItems > MaximumControls))
        {
            return Task.FromResult(new ToolResult(false, "max_items must be between 1 and 120."));
        }

        try
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
                return Task.FromResult(new ToolResult(false, "No foreground application window was detected."));

            _ = GetWindowThreadProcessId(handle, out var processId);
            var processName = "unknown";
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The foreground application may close while the metadata is being read.
            }

            using var automation = new UIA3Automation();
            var root = automation.FromHandle(handle);
            var windowTitle = Safe(() => root.Name, string.Empty);
            var descendants = root.FindAllDescendants();
            var controls = new List<ControlInfo>(Math.Min(descendants.Length, maxItems));

            foreach (var element in descendants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (controls.Count >= maxItems)
                    break;

                try
                {
                    var name = Safe(() => element.Name, string.Empty);
                    var automationId = Safe(() => element.AutomationId, string.Empty);
                    var controlType = Safe(() => element.ControlType.ToString(), "Unknown");
                    var bounds = Safe(() => element.BoundingRectangle, System.Drawing.Rectangle.Empty);
                    var enabled = Safe(() => element.IsEnabled, false);
                    var offscreen = Safe(() => element.IsOffscreen, true);

                    // Omit invisible controls and nameless nodes that cannot be reliably targeted.
                    if (offscreen || (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(automationId)))
                        continue;

                    if (!string.IsNullOrEmpty(query) &&
                        !name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                        !automationId.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                        !controlType.Contains(query, StringComparison.OrdinalIgnoreCase))
                        continue;

                    controls.Add(new ControlInfo(
                        name,
                        automationId,
                        controlType,
                        enabled,
                        $"{bounds.X},{bounds.Y},{bounds.Width},{bounds.Height}"));
                }
                catch
                {
                    // Ignore individual elements which disappear or reject UIA metadata reads.
                }
            }

            // Do not return a potentially stale window tree if focus changed during inspection.
            if (GetForegroundWindow() != handle)
                return Task.FromResult(new ToolResult(false,
                    "The foreground window changed during inspection. Please retry on the intended application."));

            var payload = new
            {
                processName,
                processId,
                windowTitle,
                inspectedControls = controls.Count,
                controls
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            return Task.FromResult(new ToolResult(true, json));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or
                                   System.ComponentModel.Win32Exception)
        {
            return Task.FromResult(new ToolResult(false,
                "Windows UI Automation inspection failed safely: " + ex.GetBaseException().Message));
        }
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    private sealed record ControlInfo(
        string Name,
        string AutomationId,
        string ControlType,
        bool Enabled,
        string Bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
