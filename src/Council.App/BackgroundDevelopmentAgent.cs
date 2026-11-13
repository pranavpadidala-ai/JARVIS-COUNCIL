using Council.Core.Providers;
using Council.Dev.Tasks;
using Council.Execution.Tools;

namespace Council.App;

/// <summary>
/// Low-CPU local worker that runs only development tasks with a fresh,
/// one-shot approval explicitly granted by the user in JARVIS.
/// </summary>
public sealed class BackgroundDevelopmentAgent
{
    private const string MutexName = @"Local\JARVIS-Council-Development-Agent";
    private readonly AutonomousDevelopmentTool _development;
    private readonly TaskManager _tasks;
    private readonly string _logPath;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _logGate = new(1, 1);

    public BackgroundDevelopmentAgent(
        IAIProvider provider,
        string workspaceRoot,
        string? taskStorePath = null,
        string? logPath = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var storePath = taskStorePath ??
            Environment.GetEnvironmentVariable("JARVIS_TASK_STORE_PATH");
        _tasks = new TaskManager(new TaskStore(storePath));
        _development = new AutonomousDevelopmentTool(provider, workspaceRoot, storePath);
        _logPath = Path.GetFullPath(logPath ??
            Path.Combine(localData, "JARVIS", "Agent", "agent.log"));

        var configuredSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("JARVIS_AGENT_POLL_SECONDS"), out var seconds)
            ? Math.Clamp(seconds, 15, 300)
            : 30;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(configuredSeconds);
        if (_pollInterval < TimeSpan.FromSeconds(5) || _pollInterval > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var mutex = new Mutex(initiallyOwned: false, MutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                await WriteLogAsync("Another JARVIS development agent instance is already running.", cancellationToken);
                return;
            }

            await _tasks.InitializeAsync(cancellationToken);
            var recovered = await _tasks.RecoverInterruptedTasksAsync(cancellationToken);
            await WriteLogAsync(
                $"Agent started. Provider configured. Recovered interrupted tasks: {recovered}. Poll interval: {_pollInterval.TotalSeconds:0}s.",
                cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await RunOneCycleAsync(cancellationToken);
                await Task.Delay(_pollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteLogAsync("Agent stopped by cancellation.", CancellationToken.None);
        }
        catch (Exception ex)
        {
            await WriteLogAsync(
                "Agent stopped after an unhandled error: " + ex.GetType().Name + ": " + ex.Message,
                CancellationToken.None);
            throw;
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }

    /// <summary>Executes at most one approved task; useful for diagnostics and tests.</summary>
    public async Task<ToolResult> RunOneCycleAsync(CancellationToken cancellationToken = default)
    {
        var result = await _development.ExecuteAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = "run_approved"
            },
            cancellationToken);

        if (!result.Success)
            await WriteLogAsync("Approved task cycle blocked or failed. Review task status in JARVIS.", cancellationToken);
        else if (!result.Message.Contains("No approved development tasks are waiting.", StringComparison.OrdinalIgnoreCase))
            await WriteLogAsync("An approved development task cycle finished. Review its task record for details.", cancellationToken);

        return result;
    }

    private async Task WriteLogAsync(string message, CancellationToken cancellationToken)
    {
        await _logGate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_logPath)!;
            Directory.CreateDirectory(directory);
            if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1_000_000)
            {
                var backup = _logPath + ".1";
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(_logPath, backup);
            }

            var line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
            await File.AppendAllTextAsync(_logPath, line, cancellationToken);
        }
        finally
        {
            _logGate.Release();
        }
    }
}
