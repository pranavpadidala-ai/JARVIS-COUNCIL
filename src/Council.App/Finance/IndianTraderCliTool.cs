using System.Diagnostics;
using System.Text.RegularExpressions;
using Council.Execution.Tools;

namespace Council.App.Finance;

/// <summary>
/// Runs the pinned Indian Trader CLI through a narrow, read-only command allowlist.
/// No shell is used; every argument is passed as a distinct process argument.
/// </summary>
public sealed class IndianTraderCliTool : ITool
{
    private const int MaxOutputCharacters = 24_000;
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(45);
    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "clock", "pulse", "quote", "chart", "chain", "flows", "global", "news",
        "movers", "policy", "scan", "screen", "insiders", "brief", "risk", "budget"
    };
    private readonly string _scriptPath;
    private readonly string _pythonExecutable;

    public IndianTraderCliTool(string? scriptPath = null, string? pythonExecutable = null)
    {
        var configuredPath = scriptPath ?? Environment.GetEnvironmentVariable("JARVIS_INDIAN_TRADER_PATH");
        if (string.IsNullOrWhiteSpace(configuredPath))
            configuredPath = Path.Combine(Directory.GetCurrentDirectory(), "integrations", "indian-trader", "it.py");
        else if (Directory.Exists(configuredPath))
            configuredPath = Path.Combine(configuredPath, "it.py");
        _scriptPath = Path.GetFullPath(configuredPath);
        _pythonExecutable = string.IsNullOrWhiteSpace(pythonExecutable)
            ? Environment.GetEnvironmentVariable("JARVIS_PYTHON_EXECUTABLE") ?? "python"
            : pythonExecutable;
    }

    public string Name => "indian_trader";
    public string Description =>
        "Optional Indian Trader read-only NSE/BSE research and analytics tool. Actions: clock, pulse, quote, chart, chain, flows, global, news, movers, policy, scan, screen, insiders, brief, risk, budget. " +
        "Use symbol for quote/chart/chain; interval for chart (5m, 15m, 1h, 1d); pick for chain (CE:ATM or PE:ATM); preset=momentum and universe=nifty50|nifty100|fno for screen. " +
        "Requires Python 3.8+ and the initialized integrations/indian-trader submodule. Public data can be delayed or unavailable. Read-only allowlist: no broker login, order placement, config changes, paper-trade writes, or arbitrary shell commands.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("action", out var action) || string.IsNullOrWhiteSpace(action))
            return new ToolResult(false, "indian_trader requires an action.");
        action = action.Trim().ToLowerInvariant();
        if (!AllowedActions.Contains(action))
            return new ToolResult(false, "That Indian Trader action is not enabled. JARVIS exposes read-only research commands only.");

        var allowedArguments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "action", "symbol", "interval", "pick", "preset", "universe" };
        if (arguments.Keys.Any(key => !allowedArguments.Contains(key)))
            return new ToolResult(false, "Unsupported argument. Only action, symbol, interval, pick, preset and universe are accepted.");

        var needsSymbol = action is "quote" or "chart" or "chain";
        var symbol = arguments.TryGetValue("symbol", out var rawSymbol) ? rawSymbol.Trim().ToUpperInvariant() : string.Empty;
        if (needsSymbol && (symbol.Length is < 1 or > 20 || !Regex.IsMatch(symbol, "^[A-Z0-9][A-Z0-9._&-]*$", RegexOptions.CultureInvariant)))
            return new ToolResult(false, "This action requires a valid symbol containing only letters, numbers, dot, ampersand, underscore or hyphen.");
        if (!needsSymbol && arguments.ContainsKey("symbol"))
            return new ToolResult(false, "symbol is accepted only for quote, chart and chain actions.");

        var interval = arguments.TryGetValue("interval", out var rawInterval) ? rawInterval.Trim().ToLowerInvariant() : string.Empty;
        if (interval.Length > 0 && (action != "chart" || interval is not ("5m" or "15m" or "1h" or "1d")))
            return new ToolResult(false, "interval is supported only for chart and must be 5m, 15m, 1h or 1d.");
        var pick = arguments.TryGetValue("pick", out var rawPick) ? rawPick.Trim().ToUpperInvariant() : string.Empty;
        if (pick.Length > 0 && (action != "chain" || !Regex.IsMatch(pick, "^(CE|PE):ATM$", RegexOptions.CultureInvariant)))
            return new ToolResult(false, "pick is supported only for chain and must be CE:ATM or PE:ATM.");
        var preset = arguments.TryGetValue("preset", out var rawPreset) ? rawPreset.Trim().ToLowerInvariant() : string.Empty;
        if (preset.Length > 0 && (action != "screen" || preset != "momentum"))
            return new ToolResult(false, "Only preset=momentum is currently supported for screen.");
        var universe = arguments.TryGetValue("universe", out var rawUniverse) ? rawUniverse.Trim().ToLowerInvariant() : string.Empty;
        if (universe.Length > 0 && (action != "screen" || universe is not ("nifty50" or "nifty100")))
            return new ToolResult(false, "universe is supported only for screen and must be nifty50, nifty100 or fno.");

        if (!File.Exists(_scriptPath))
            return new ToolResult(false, "Indian Trader is not initialized. Run scripts/Initialize-ExternalIntegrations.ps1 from the JARVIS repository, or set JARVIS_INDIAN_TRADER_PATH to the folder containing it.py.");

        var start = new ProcessStartInfo
        {
            FileName = _pythonExecutable,
            WorkingDirectory = Path.GetDirectoryName(_scriptPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(_scriptPath);
        start.ArgumentList.Add(action);
        if (needsSymbol) start.ArgumentList.Add(symbol);
        if (interval.Length > 0) { start.ArgumentList.Add("--interval"); start.ArgumentList.Add(interval); }
        if (pick.Length > 0) { start.ArgumentList.Add("--pick"); start.ArgumentList.Add(pick); }
        if (action == "screen")
        {
            start.ArgumentList.Add("--preset"); start.ArgumentList.Add(preset.Length == 0 ? "momentum" : preset);
            if (universe.Length > 0) { start.ArgumentList.Add("--list"); start.ArgumentList.Add(universe); }
        }
        start.ArgumentList.Add("--json");

        try
        {
            using var process = new Process { StartInfo = start };
            if (!process.Start()) return new ToolResult(false, "Could not start the Python process.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ExecutionTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                if (cancellationToken.IsCancellationRequested) throw;
                return new ToolResult(false, "Indian Trader exceeded the 45-second execution limit and was stopped.");
            }
            var output = await stdoutTask;
            var error = await stderrTask;
            var message = string.IsNullOrWhiteSpace(output) ? error.Trim() : output.Trim();
            if (message.Length > MaxOutputCharacters)
                message = message[..MaxOutputCharacters] + "\n[Output truncated by JARVIS.]";
            if (process.ExitCode != 0)
                return new ToolResult(false, string.IsNullOrWhiteSpace(message) ? "Indian Trader exited with code " + process.ExitCode + "." : message);
            return new ToolResult(true, string.IsNullOrWhiteSpace(message) ? "Indian Trader completed without output." : message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            return new ToolResult(false, "Indian Trader could not run safely: " + ex.Message);
        }
    }
}
