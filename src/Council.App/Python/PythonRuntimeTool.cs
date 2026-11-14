using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Python;

/// <summary>
/// Provides bounded Python environment and source inspection through a small
/// stdlib-only worker. Python source is parsed/compiled for syntax checking,
/// never executed by this tool.
/// </summary>
public sealed class PythonRuntimeTool : ITool
{
    private static readonly TimeSpan WorkerTimeout = TimeSpan.FromSeconds(15);
    private readonly string _workspaceRoot;

    public PythonRuntimeTool(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("A workspace root is required.", nameof(workspaceRoot));

        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public string Name => "python_runtime";

    public string Description =>
        "Use the installed Python runtime without writing C# substitutes. Fixed actions: " +
        "health (Python version/executable), inspect_modules (allowlisted package availability), " +
        "engineering_tools (Abaqus/MATLAB/ETABS/AutoCAD command availability), " +
        "check_syntax (required source OR workspace-relative .py path), " +
        "inspect_file (required workspace-relative .py path; returns imports/classes/functions). " +
        "The worker uses Python standard library only, never imports or executes inspected user code, " +
        "and confines file reads to the configured JARVIS workspace. Do not use it to run arbitrary commands.";

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!arguments.TryGetValue("action", out var actionValue) ||
            string.IsNullOrWhiteSpace(actionValue))
            return new ToolResult(false, "Required argument: action.");

        var action = actionValue.Trim().ToLowerInvariant();
        var allowed = action switch
        {
            "health" or "inspect_modules" or "engineering_tools" => new[] { "action" },
            "check_syntax" => new[] { "action", "source", "path" },
            "inspect_file" => new[] { "action", "path" },
            _ => null
        };

        if (allowed is null)
            return new ToolResult(false, "Available actions: health, inspect_modules, engineering_tools, check_syntax, inspect_file.");

        if (arguments.Keys.Any(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase)))
            return new ToolResult(false, "Unexpected arguments were supplied for this Python action.");

        if (action == "check_syntax")
        {
            var hasSource = arguments.TryGetValue("source", out var source);
            var hasPath = arguments.TryGetValue("path", out var path);
            if (hasSource == hasPath)
                return new ToolResult(false, "check_syntax requires exactly one of source or path.");
            if (hasSource && source!.Length > 512_000)
                return new ToolResult(false, "Python source exceeds the 512 KB limit.");
            if (hasPath && string.IsNullOrWhiteSpace(path))
                return new ToolResult(false, "A workspace-relative Python file path is required.");
        }

        if (action == "inspect_file" &&
            (!arguments.TryGetValue("path", out var inspectPath) || string.IsNullOrWhiteSpace(inspectPath)))
            return new ToolResult(false, "inspect_file requires a workspace-relative .py path.");

        var worker = ResolveWorkerPath();
        if (worker is null)
            return new ToolResult(false, "Python worker file was not found. Rebuild JARVIS from the repository so the worker is copied to the application output.");

        var python = ResolvePython();
        if (python is null)
            return new ToolResult(false, "Python 3.10 or newer was not found. Set JARVIS_PYTHON_EXECUTABLE to a valid python.exe path.");

        var request = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = action,
            ["workspace"] = _workspaceRoot
        };
        if (arguments.TryGetValue("source", out var suppliedSource))
            request["source"] = suppliedSource;
        if (arguments.TryGetValue("path", out var suppliedPath))
            request["path"] = suppliedPath;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = python.FileName,
            WorkingDirectory = _workspaceRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var prefixArgument in python.PrefixArguments)
            process.StartInfo.ArgumentList.Add(prefixArgument);
        process.StartInfo.ArgumentList.Add(worker);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
                return new ToolResult(false, "Could not start the Python worker.");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request));
            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(WorkerTimeout);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            var error = await errorTask;

            if (output.Length > 1_000_000)
                return new ToolResult(false, "Python worker returned an oversized response.");
            if (process.ExitCode != 0)
                return new ToolResult(false, "Python worker failed: " + Trim(error, 2000));
            if (string.IsNullOrWhiteSpace(output))
                return new ToolResult(false, "Python worker returned no response: " + Trim(error, 1000));

            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var success = root.TryGetProperty("success", out var successElement) &&
                          successElement.ValueKind == JsonValueKind.True;
            var message = root.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? "Python worker completed."
                : "Python worker completed.";
            var data = root.TryGetProperty("data", out var dataElement)
                ? JsonSerializer.Serialize(dataElement, new JsonSerializerOptions { WriteIndented = true })
                : "{}";

            return new ToolResult(success, message + Environment.NewLine + data);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ToolResult(false, $"Python worker exceeded the {WorkerTimeout.TotalSeconds:0}-second timeout.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception)
        {
            TryKill(process);
            return new ToolResult(false, "Python worker could not complete safely: " + ex.GetBaseException().Message);
        }
    }

    private string? ResolveWorkerPath()
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_PYTHON_WORKER");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return Path.GetFullPath(configured);

        var outputPath = Path.Combine(AppContext.BaseDirectory, "python", "jarvis_runtime", "worker.py");
        if (File.Exists(outputPath))
            return outputPath;

        var workspacePath = Path.Combine(_workspaceRoot, "python", "jarvis_runtime", "worker.py");
        return File.Exists(workspacePath) ? workspacePath : null;
    }

    private (string FileName, string[] PrefixArguments)? ResolvePython()
    {
        var configured = Environment.GetEnvironmentVariable("JARVIS_PYTHON_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return (Path.GetFullPath(configured), []);

        var localAiPython = Path.Combine(_workspaceRoot, "Models", "LocalAI", "piper-venv", "Scripts", "python.exe");
        if (File.Exists(localAiPython))
            return (localAiPython, []);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installedPython = Path.Combine(localAppData, "Programs", "Python", "Python311", "python.exe");
        if (File.Exists(installedPython))
            return (installedPython, []);

        if (OperatingSystem.IsWindows())
            return ("py.exe", ["-3.11"]);

        return ("python3", []);
    }

    private static string Trim(string value, int maximum) =>
        value.Length <= maximum ? value : value[..maximum] + "…";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
