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
        var checkpoints = new GitCheckpointManager(terminal, root);
        _orchestrator = new DevelopmentOrchestrator(loop, executor, checkpoints);
    }

    public string Name => "development_tasks";

    public string Description =>
        "Persistent autonomous software-development task manager for the configured JARVIS workspace. " +
        "Actions: create_task (required title, optional description); list_tasks (no extra arguments); " +
        "run_task (required task_id GUID, optional max_attempts from 1 to 5; defaults to 2). " +
        "Running a task requires a clean Git working tree. JARVIS creates a checkpoint, plans and applies safe workspace changes, " +
        "runs dotnet build and dotnet test, commits only after validation succeeds, and rolls back failed attempts. " +
        "A task is never run merely by creating it; the user must explicitly ask JARVIS to run it.";

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("action", out var requestedAction))
            return new ToolResult(false, "Required argument: action.");

        try
        {
            await EnsureInitializedAsync(cancellationToken);
            return requestedAction.Trim().ToLowerInvariant() switch
            {
                "create_task" => await CreateTaskAsync(arguments, cancellationToken),
                "list_tasks" => ListTasks(arguments),
                "run_task" => await RunTaskAsync(arguments, cancellationToken),
                _ => new ToolResult(false, "Unknown development task action. Use create_task, list_tasks, or run_task.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidOperationException or ArgumentException or
                                   System.Security.Cryptography.CryptographicException)
        {
            return new ToolResult(false, "Development task operation failed safely: " + ex.Message);
        }
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
            "The task has not been run; explicitly ask JARVIS to run this task ID.");
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
            output.Append($"{task.Id} | {task.Status} | attempts: {task.AttemptCount} | {task.Title}");
            if (!string.IsNullOrWhiteSpace(task.CurrentStep))
                output.AppendLine().Append("  Current step: ").Append(task.CurrentStep);
        }
        return new ToolResult(true, output.ToString());
    }

    private async Task<ToolResult> RunTaskAsync(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action", "task_id", "max_attempts") ||
            !args.TryGetValue("task_id", out var idText) ||
            !Guid.TryParse(idText, out var id))
            return new ToolResult(false, "run_task requires task_id as a valid task GUID; max_attempts is optional.");

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
            var result = await _orchestrator.ExecuteAsync(task, maxAttempts, cancellationToken);
            return new ToolResult(result.Success,
                (result.Success ? "Development task completed. " : "Development task failed. ") +
                result.Message + Environment.NewLine +
                "Task ID: " + task.Id + " | Status: " + task.Status +
                " | Attempts: " + task.AttemptCount);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private static bool HasOnly(Dictionary<string, string> args, params string[] allowed) =>
        args.Keys.All(key => allowed.Contains(key, StringComparer.OrdinalIgnoreCase));
}
