using Council.AI.Providers;
using Council.Core.Models;
using Council.Core.Orchestration;
using Council.Core.Providers;
using Council.Dev.Actions;
using Council.Dev.Execution;
using Council.Dev.Loop;
using Council.Dev.Planning;
using Council.Dev.Runner;
using Council.Dev.Tasks;
using Council.Dev.Terminal;
using Council.Dev.Validation;
using Council.Dev.Workspace;
using Council.Execution;
using Council.Execution.Tools;

// ============================================================
// JARVIS COUNCIL - AI PROVIDERS
// ============================================================

var providers = new List<IAIProvider>();

// Always keep Mock available for offline testing.
var mockProvider = new MockAIProvider();
providers.Add(mockProvider);

// Add Gemini when an API key is configured.
var geminiApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

GeminiProvider? geminiProvider = null;

if (!string.IsNullOrWhiteSpace(geminiApiKey))
{
    geminiProvider = new GeminiProvider(
        geminiApiKey,
        model: "gemini-3.6-flash");

    providers.Add(geminiProvider);
}
else
{
    Console.WriteLine(
        "Warning: GEMINI_API_KEY is not configured.");
}

// ============================================================
// JARVIS COUNCIL TEST
// ============================================================

var council = new CouncilOrchestrator(providers);

var request = new AIRequest(
    Messages:
    [
        new AIMessage(
            "user",
            "What is the purpose of JARVIS Council?")
    ]);

var responses = await council.AskAllAsync(request);

foreach (var response in responses)
{
    Console.WriteLine();
    Console.WriteLine($"Provider : {response.Provider}");
    Console.WriteLine($"Model    : {response.Model}");
    Console.WriteLine($"Response : {response.Content}");
    Console.WriteLine($"Duration : {response.Duration}");
}

// ============================================================
// TOOL REGISTRY TEST
// ============================================================

Console.WriteLine();
Console.WriteLine("=== Tool Test ===");

var toolRegistry = new ToolRegistry();

toolRegistry.Register(new CreateFolderTool());

if (toolRegistry.TryGet("create_folder", out var createFolderTool))
{
    var testPath = Path.Combine(
        Environment.CurrentDirectory,
        "jarvis-test-folder");

    var toolResult = await createFolderTool.ExecuteAsync(
        new Dictionary<string, string>
        {
            ["path"] = testPath
        });

    Console.WriteLine($"Tool    : {createFolderTool.Name}");
    Console.WriteLine($"Success : {toolResult.Success}");
    Console.WriteLine($"Message : {toolResult.Message}");
}
else
{
    Console.WriteLine(
        "ERROR: create_folder tool was not found.");
}

// ============================================================
// AUTONOMOUS DEVELOPMENT SYSTEM
// ============================================================

Console.WriteLine();
Console.WriteLine("============================================================");
Console.WriteLine("        JARVIS AUTONOMOUS DEVELOPMENT SYSTEM");
Console.WriteLine("============================================================");

// The repository root is two levels above:
// src/Council.App/bin/Debug/net8.0
//
// Directory.GetCurrentDirectory() when running through
// dotnet run --project src/Council.App is normally the
// repository root, so use it as the workspace.

var workspaceRoot = Directory.GetCurrentDirectory();

Console.WriteLine($"Workspace : {workspaceRoot}");

// ------------------------------------------------------------
// Persistent task storage
// ------------------------------------------------------------

var taskStore = new TaskStore();

var taskManager = new TaskManager(taskStore);

await taskManager.InitializeAsync();

// ------------------------------------------------------------
// Terminal
// ------------------------------------------------------------

var terminalController = new TerminalController();

// ------------------------------------------------------------
// Workspace
// ------------------------------------------------------------

var workspaceExecutor =
    new SafeWorkspaceExecutor(workspaceRoot);

// ------------------------------------------------------------
// Development actions
// ------------------------------------------------------------

var developmentActionExecutor =
    new DevelopmentActionExecutor(
        workspaceExecutor,
        terminalController,
        workspaceRoot);

// ------------------------------------------------------------
// Build + test validation
// ------------------------------------------------------------

var buildTestExecutor =
    new BuildTestExecutor(
        terminalController,
        workspaceRoot);

// ------------------------------------------------------------
// AI planner
// ------------------------------------------------------------

IAIProvider planningProvider;

if (geminiProvider is not null)
{
    planningProvider = geminiProvider;
    Console.WriteLine("Planner   : Gemini");
}
else
{
    planningProvider = mockProvider;
    Console.WriteLine("Planner   : Mock");
}

// ------------------------------------------------------------
// Autonomous executor
// ------------------------------------------------------------

var repositoryContextProvider =
    new RepositoryContextProvider(
        workspaceRoot);

var developmentPlanner =
    new AIDevelopmentPlanner(
        planningProvider,
        repositoryContextProvider);

var autonomousExecutor =
    new AutonomousDevelopmentExecutor(
        developmentPlanner,
        developmentActionExecutor,
        buildTestExecutor,
        taskManager);

// ------------------------------------------------------------
// Development loop
// ------------------------------------------------------------

var developmentLoop =
    new DevelopmentLoop(
        taskManager,
        new TaskRunner(taskManager));

// ------------------------------------------------------------
// Development orchestrator
// ------------------------------------------------------------

var developmentOrchestrator =
    new DevelopmentOrchestrator(
        developmentLoop,
        autonomousExecutor);

// ============================================================
// CREATE DEVELOPMENT TASK
// ============================================================

var developmentTask =
    await taskManager.CreateAsync(
        "Autonomous developer smoke test",
        "Inspect the repository and verify that the JARVIS Council " +
        "development system can autonomously execute a safe plan " +
        "and validate the repository.");

Console.WriteLine();
Console.WriteLine($"Task ID   : {developmentTask.Id}");
Console.WriteLine($"Task      : {developmentTask.Title}");
Console.WriteLine();

// ============================================================
// EXECUTE
// ============================================================

try
{
    var developmentResult =
        await developmentOrchestrator.ExecuteAsync(
            developmentTask,
            maxAttempts: 3);

    Console.WriteLine();
    Console.WriteLine("=== Autonomous Development Result ===");
    Console.WriteLine($"Success   : {developmentResult.Success}");
    Console.WriteLine($"Message   : {developmentResult.Message}");

    var finalTask =
        taskManager.Find(developmentTask.Id);

    if (finalTask is not null)
    {
        Console.WriteLine();
        Console.WriteLine("=== Task State ===");
        Console.WriteLine($"Status    : {finalTask.Status}");
        Console.WriteLine($"Attempts  : {finalTask.AttemptCount}");
        Console.WriteLine($"Step      : {finalTask.CurrentStep}");
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine();
    Console.WriteLine(
        "Autonomous development was cancelled.");
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine(
        "Autonomous development failed.");

    Console.WriteLine(
        $"Error     : {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("============================================================");
Console.WriteLine("JARVIS Council execution finished.");
Console.WriteLine("============================================================");

