using System.Text;
using Council.Core.Providers;
using Council.Dev.Actions;
using Council.Dev.Execution;
using Council.Dev.Git;
using Council.Dev.Loop;
using Council.Dev.Planning;
using Council.Dev.Runner;
using Council.Dev.Tasks;
using Council.Dev.Terminal;
using Council.Dev.Validation;
using Council.Dev.Workspace;
using Council.Execution.Tools;
using DevTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.App;

/// <summary>
/// Connects the persistent development task queue to the interactive JARVIS
/// tool loop. A task is run only when the user explicitly asks JARVIS to run it.
/// Each attempt requires a clean Git working tree, creates a checkpoint, runs
/// the planned changes, validates with dotnet build/test, and commits only on success.
/// </summary>
public sealed class AutonomousDevelopmentTool : ITool
{
    private readonly TaskManager _tasks;
    private readonly DevelopmentOrchestrator _orchestrator;
    private readonly GitCheckpointManager _checkpoints;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private bool _initialized;

    public AutonomousDevelopmentTool(
        IAIProvider provider,
        string workspaceRoot,
        string? taskStorePath = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot));

        var root = Path.GetFullPath(workspaceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("Development workspace does not exist: " + root);

        _tasks = new TaskManager(new TaskStore(taskStorePath));
        var terminal = new TerminalController();
        var workspace = new SafeWorkspaceExecutor(root);
        var actions = new DevelopmentActionExecutor(workspace, terminal, root);
        var validation = new BuildTestExecutor(terminal, root);
        var context = new RepositoryContextProvider(root);
        var planner = new AIDevelopmentPlanner(provider, context);
        var executor = new AutonomousDevelopmentExecutor(planner, actions, validation, _tasks);
        var loop = new DevelopmentLoop(_tasks, new TaskRunner(_tasks));
        _checkpoints = new GitCheckpointManager(terminal, root);
        _orchestrator = new DevelopmentOrchestrator(loop, executor, _checkpoints);
    }

    public string Name => "development_tasks";

    public string Description =>
        "Persistent autonomous software-development task manager for the configured JARVIS workspace. " +
        "Actions: create_task (required title, optional description); list_tasks; approve_task (required task_id GUID; explicitly authorizes one background run); revoke_approval (required task_id GUID); run_task (required task_id GUID, explicit immediate user request, optional max_attempts 1-5); run_approved (internal background-host action only). " +
        "Creating a task never approves or runs it. The background host claims only tasks with a fresh, unconsumed user approval; each approval is one-shot and a failed/interrupted task needs fresh approval. " +
        "Running a task requires a clean Git working tree and a cross-process execution lock. JARVIS creates a checkpoint, plans and applies safe workspace changes, " +
        "runs dotnet build and dotnet test, commits only after validation succeeds, and rolls back failed attempts.";

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("action", out var requestedAction))
            return new ToolResult(false, "Required argument: action.");

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            await _tasks.RefreshAsync(cancellationToken);
            return requestedAction.Trim().ToLowerInvariant() switch
            {
                "create_task" => await CreateTaskAsync(arguments, cancellationToken),
                "list_tasks" => ListTasks(arguments),
                "approve_task" => await ApproveTaskAsync(arguments, cancellationToken),
                "revoke_approval" => await RevokeApprovalAsync(arguments, cancellationToken),
                "run_task" => await RunTaskAsync(arguments, cancellationToken),
                "run_approved" => await RunNextApprovedTaskAsync(arguments, cancellationToken),
                _ => new ToolResult(false, "Unknown development task action. Use create_task, list_tasks, approve_task, revoke_approval, or run_task.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidOperationException or ArgumentException or
                                   System.Text.Json.JsonException or InvalidDataException or
                                   System.Security.Cryptography.CryptographicException)
        {
            return new ToolResult(false, "Development task operation failed safely: " + ex.Message);
        }
    }

    /// <summary>
    /// Recovers interrupted work only after acquiring the same global execution
    /// lock used by foreground runs, so agent startup cannot pause a live task.
    /// </summary>
    public async Task<int> RecoverInterruptedTasksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await using var executionLock = await AcquireGlobalExecutionLockAsync(cancellationToken);
        await _tasks.RefreshAsync(cancellationToken);
        return await _tasks.RecoverInterruptedTasksAsync(cancellationToken);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await _tasks.InitializeAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private async Task<ToolResult> CreateTaskAsync(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action", "title", "description") ||
            !args.TryGetValue("title", out var title) ||
            string.IsNullOrWhiteSpace(title))
            return new ToolResult(false, "create_task requires a title; description is optional.");

        if (title.Trim().Length > 200)
            return new ToolResult(false, "Task title is limited to 200 characters.");

        var description = args.TryGetValue("description", out var text) ? text.Trim() : string.Empty;
        if (description.Length > 8000)
            return new ToolResult(false, "Task description is limited to 8,000 characters.");

        var task = await _tasks.CreateAsync(title, description, cancellationToken);
        return new ToolResult(true,
            $"Created persistent development task {task.Id}. Status: Pending. " +
            "The task has not been approved or run. Use approve_task only when you explicitly authorize the local background agent to execute it, or explicitly ask JARVIS to run it now.");
    }

    private ToolResult ListTasks(Dictionary<string, string> args)
    {
        if (!HasOnly(args, "action"))
            return new ToolResult(false, "list_tasks accepts only action.");

        var tasks = _tasks.Tasks
            .OrderByDescending(task => task.UpdatedAtUtc)
            .Take(50)
            .ToArray();

        if (tasks.Length == 0)
            return new ToolResult(true, "No saved development tasks.");

        var output = new StringBuilder("Most recent development tasks:");
        foreach (var task in tasks)
        {
            output.AppendLine();
            var approval = task.IsApproved && task.ApprovalConsumedAtUtc is null ? "APPROVED / waiting" : "not approved / consumed";
            output.Append($"{task.Id} | {task.Status} | {approval} | attempts: {task.AttemptCount} | {task.Title}");
            if (!string.IsNullOrWhiteSpace(task.CurrentStep))
                output.AppendLine().Append("  Current step: ").Append(task.CurrentStep);
        }
        return new ToolResult(true, output.ToString());
    }

    private async Task<ToolResult> ApproveTaskAsync(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action", "task_id") ||
            !args.TryGetValue("task_id", out var idText) ||
            !Guid.TryParse(idText, out var id))
            return new ToolResult(false, "approve_task requires task_id as a valid task GUID.");

        var approved = await _tasks.ApproveAsync(id, cancellationToken);
        return new ToolResult(approved, approved
            ? $"Task {id} is approved for one background execution. The local agent still requires a clean Git tree and validates before committing."
            : "Approval was not applied. The task may not exist, may already be running, or may already be completed.");
    }

    private async Task<ToolResult> RevokeApprovalAsync(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action", "task_id") ||
            !args.TryGetValue("task_id", out var idText) ||
            !Guid.TryParse(idText, out var id))
            return new ToolResult(false, "revoke_approval requires task_id as a valid task GUID.");

        var revoked = await _tasks.RevokeApprovalAsync(id, cancellationToken);
        return new ToolResult(revoked, revoked
            ? $"Background approval for task {id} was revoked."
            : "Approval could not be revoked. The task may not exist or may already be running.");
    }

    private async Task<ToolResult> RunNextApprovedTaskAsync(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action"))
            return new ToolResult(false, "run_approved is an internal background-host action and accepts only action.");

        var task = await _tasks.ClaimNextApprovedTaskAsync(cancellationToken);
        if (task is null)
            return new ToolResult(true, "No approved development tasks are waiting.");

        return await RunTaskAsync(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = "run_task",
                ["task_id"] = task.Id.ToString(),
                ["max_attempts"] = "2"
            },
            cancellationToken);
    }

    private async Task<ToolResult> RunTaskAsync(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action", "task_id", "max_attempts") ||
            !args.TryGetValue("task_id", out var idText) ||
            !Guid.TryParse(idText, out var id))
            return new ToolResult(false, "run_task requires task_id as a valid task GUID; max_attempts is optional.");

        await _tasks.RefreshAsync(cancellationToken);
        var task = _tasks.Find(id);
        if (task is null)
            return new ToolResult(false, "No saved development task matches that ID.");

        if (task.Status is DevTaskStatus.Completed or DevTaskStatus.Running)
            return new ToolResult(false, "Only Pending, Paused, or Failed tasks can be run.");

        var maxAttempts = 2;
        if (args.TryGetValue("max_attempts", out var attemptsText) &&
            (!int.TryParse(attemptsText, out maxAttempts) || maxAttempts is < 1 or > 5))
            return new ToolResult(false, "max_attempts must be an integer from 1 to 5.");

        if (!await _runGate.WaitAsync(0, cancellationToken))
            return new ToolResult(false, "Another autonomous development task is already running.");

        try
        {
            await using var executionLock = await AcquireGlobalExecutionLockAsync(cancellationToken);
            await _tasks.RefreshAsync(cancellationToken);
            task = _tasks.Find(id);
            if (task is null)
                return new ToolResult(false, "The development task no longer exists.");

            var preflight = await _checkpoints.CreateCheckpointAsync(cancellationToken);
            if (!preflight.Success || preflight.Checkpoint is null)
                return new ToolResult(false, "Autonomous execution blocked before changes: " + preflight.Message);

            var result = await _orchestrator.ExecuteAsync(task, maxAttempts, cancellationToken);
            await _tasks.RefreshAsync(cancellationToken);
            var finalTask = _tasks.Find(id) ?? task;
            return new ToolResult(result.Success,
                (result.Success ? "Development task completed. " : "Development task failed. ") +
                result.Message + Environment.NewLine +
                "Task ID: " + finalTask.Id + " | Status: " + finalTask.Status +
                " | Attempts: " + finalTask.AttemptCount);
        }
        finally
        {
            _runGate.Release();
        }
    }


    private static async Task<FileStream> AcquireGlobalExecutionLockAsync(
        CancellationToken cancellationToken)
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
            localData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directory = Path.Combine(localData, "JARVIS");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "development-execution.lock");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, bufferSize: 1, useAsync: true);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
            }
        }
    }

    private static bool HasOnly(Dictionary<string, string> args, params string[] allowed) =>
        args.Keys.All(key => allowed.Contains(key, StringComparer.OrdinalIgnoreCase));
}
