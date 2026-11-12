using Council.AI.Providers;
using Council.App.Abaqus;
using Council.App;
using Council.App.Voice;
using Council.Core.Models;
using Council.Core.Orchestration;
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
using Council.Execution;
using Council.Execution.Tools;
using Council.Memory;

// ============================================================
// JARVIS COUNCIL - AI PROVIDERS
// ============================================================

// Read-only Abaqus launcher diagnostic; does not require an AI provider.
if (args.Any(arg => string.Equals(arg, "--abaqus-status", StringComparison.OrdinalIgnoreCase)))
{
    var abaqus = new AbaqusInstallationDetector().Detect();
    Console.WriteLine("JARVIS Abaqus 2024 integration check");
    Console.WriteLine(abaqus.IsAvailable ? "Launcher: DETECTED" : "Launcher: NOT DETECTED");
    Console.WriteLine(abaqus.Message);
    if (abaqus.CommandPath is not null)
        Console.WriteLine("Path: " + abaqus.CommandPath);
    return;
}

var mockProvider = new MockAIProvider();

var geminiApiKey =
    Environment.GetEnvironmentVariable("GEMINI_API_KEY");

var openAiApiKey =
    Environment.GetEnvironmentVariable("OPENAI_API_KEY");

var selectedProvider =
    Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER");

var geminiModel =
    Environment.GetEnvironmentVariable("GEMINI_MODEL")
    ?? "gemini-3.6-flash";

var openAiModel =
    Environment.GetEnvironmentVariable("OPENAI_MODEL")
    ?? "gpt-5.2";

var ollamaModel =
    Environment.GetEnvironmentVariable("OLLAMA_MODEL")
    ?? "qwen3.5:2b-q4_K_M";

var ollamaBaseUrl =
    Environment.GetEnvironmentVariable("OLLAMA_BASE_URL")
    ?? "http://localhost:11434";

var omniRouteApiKey = Environment.GetEnvironmentVariable("OMNIROUTE_API_KEY");
var omniRouteModel = Environment.GetEnvironmentVariable("OMNIROUTE_MODEL");
var omniRouteBaseUrl = Environment.GetEnvironmentVariable("OMNIROUTE_BASE_URL")
    ?? "http://localhost:20128/v1";
var omniRouteFreeModels = Environment.GetEnvironmentVariable("OMNIROUTE_FREE_MODELS");

IAIProvider activeProvider;

switch (selectedProvider?.Trim().ToLowerInvariant())
{
    case "gemini":
        if (string.IsNullOrWhiteSpace(geminiApiKey))
            throw new InvalidOperationException(
                "GEMINI_API_KEY is required for Gemini.");

        activeProvider = new GeminiProvider(
            geminiApiKey, geminiModel);
        break;

    case "openai":
        if (string.IsNullOrWhiteSpace(openAiApiKey))
            throw new InvalidOperationException(
                "OPENAI_API_KEY is required for OpenAI.");

        activeProvider = new OpenAIProvider(
            openAiApiKey, openAiModel);
        break;

    case "ollama":
        var localContextTokens = int.TryParse(
            Environment.GetEnvironmentVariable("JARVIS_OLLAMA_CONTEXT_TOKENS"), out var configuredContext)
            ? Math.Clamp(configuredContext, 2048, 8192)
            : 4096;
        activeProvider = new OllamaProvider(
            ollamaModel, ollamaBaseUrl, contextTokens: localContextTokens);
        break;

    case "omniroute":
        if (string.IsNullOrWhiteSpace(omniRouteApiKey))
            throw new InvalidOperationException("OMNIROUTE_API_KEY is required for OmniRoute.");
        if (string.IsNullOrWhiteSpace(omniRouteModel))
            throw new InvalidOperationException("OMNIROUTE_MODEL must be an explicit provider/model ID; the auto alias is not allowed.");
        if (string.IsNullOrWhiteSpace(omniRouteFreeModels))
            throw new InvalidOperationException("OMNIROUTE_FREE_MODELS must list the exact free model IDs you have explicitly approved.");
        activeProvider = new OmniRouteProvider(
            omniRouteApiKey, omniRouteModel, omniRouteBaseUrl, omniRouteFreeModels);
        break;

    case "mock":
        activeProvider = mockProvider;
        break;

    case null or "":
        if (!string.IsNullOrWhiteSpace(geminiApiKey))
        {
            activeProvider = new GeminiProvider(
                geminiApiKey, geminiModel);
        }
        else if (!string.IsNullOrWhiteSpace(openAiApiKey))
        {
            activeProvider = new OpenAIProvider(
                openAiApiKey, openAiModel);
        }
        else
        {
            activeProvider = mockProvider;
        }
        break;

    default:
        throw new InvalidOperationException(
            $"Unknown JARVIS_AI_PROVIDER: {selectedProvider}");
}

Console.WriteLine($"Active AI provider: {activeProvider.Name}");

var providers = new List<IAIProvider>
{
    activeProvider
};

// Interactive assistant is the normal startup experience.
// Pass --dev to run the existing autonomous developer demo.
if (!args.Any(arg =>
        string.Equals(arg, "--dev", StringComparison.OrdinalIgnoreCase)))
{
    if (activeProvider.Name == "Mock")
    {
        Console.WriteLine(
            "No AI API key detected. JARVIS is running in mock mode.");
        Console.WriteLine(
            "Set GEMINI_API_KEY or OPENAI_API_KEY, or set JARVIS_AI_PROVIDER=ollama for a local model.");
    }

    var interactiveWorkspaceRoot = Directory.GetCurrentDirectory();
    var memoryEnabled = bool.TryParse(
        Environment.GetEnvironmentVariable("JARVIS_MEMORY_ENABLED"), out var enableMemory) && enableMemory;
    var memoryStore = memoryEnabled ? new ConversationMemoryStore() : null;
    IImageUnderstandingProvider? imageProvider = null;
    var visionProviderChoice = (Environment.GetEnvironmentVariable("JARVIS_VISION_PROVIDER") ?? "ollama").Trim().ToLowerInvariant();
    var localVisionEnabled =
        !bool.TryParse(Environment.GetEnvironmentVariable("JARVIS_LOCAL_VISION_ENABLED"), out var visionEnabled) ||
        visionEnabled;
    switch (visionProviderChoice)
    {
        case "gemini":
            if (string.IsNullOrWhiteSpace(geminiApiKey))
                throw new InvalidOperationException("GEMINI_API_KEY is required when JARVIS_VISION_PROVIDER=gemini.");
            var cloudVisionModel = Environment.GetEnvironmentVariable("GEMINI_VISION_MODEL") ?? "gemini-3.8-flash";
            imageProvider = new GeminiProvider(geminiApiKey, cloudVisionModel);
            Console.WriteLine($"Cloud vision/document reader: Gemini / {cloudVisionModel}. Screen captures and PDFs are sent to Google and may use API quota.");
            break;
        case "ollama":
            if (localVisionEnabled)
            {
                var localVisionModel = Environment.GetEnvironmentVariable("JARVIS_VISION_MODEL") ?? "moondream";
                imageProvider = new OllamaVisionProvider(localVisionModel, ollamaBaseUrl, contextTokens: 2048);
                Console.WriteLine($"Local vision: Ollama / {localVisionModel} (install with scripts/Install-LocalModels.ps1). Full-PDF OCR is not supported locally.");
            }
            else Console.WriteLine("Vision disabled by JARVIS_LOCAL_VISION_ENABLED=false.");
            break;
        case "none":
            Console.WriteLine("Vision provider disabled.");
            break;
        default:
            throw new InvalidOperationException("JARVIS_VISION_PROVIDER must be ollama, gemini, or none.");
    }

    GeminiCloudSpeechProvider? cloudSpeechProvider = null;
    var audioProviderChoice = (Environment.GetEnvironmentVariable("JARVIS_AUDIO_PROVIDER") ?? "local").Trim().ToLowerInvariant();
    switch (audioProviderChoice)
    {
        case "gemini":
            if (string.IsNullOrWhiteSpace(geminiApiKey))
                throw new InvalidOperationException("GEMINI_API_KEY is required when JARVIS_AUDIO_PROVIDER=gemini.");
            var transcribeModel = Environment.GetEnvironmentVariable("GEMINI_TRANSCRIBE_MODEL") ?? "gemini-3.5-transcribe";
            var ttsModel = Environment.GetEnvironmentVariable("GEMINI_TTS_MODEL") ?? "gemini-3.8-flash-lite-tts";
            var ttsVoice = Environment.GetEnvironmentVariable("GEMINI_TTS_VOICE") ?? "Kore";
            cloudSpeechProvider = new GeminiCloudSpeechProvider(geminiApiKey, transcribeModel, ttsModel, ttsVoice);
            Console.WriteLine($"Cloud ears/mouth: Gemini transcription ({transcribeModel}) + TTS ({ttsModel}, voice {ttsVoice}). Microphone audio and speech text are sent to Google and may use API quota.");
            break;
        case "local":
            Console.WriteLine("Local ears/mouth: Whisper small multilingual + Piper neural speech (on demand).");
            break;
        default:
            throw new InvalidOperationException("JARVIS_AUDIO_PROVIDER must be local or gemini.");
    }
    Console.WriteLine("RAM profile: one Ollama model at a time; local specialist teams run sequentially.");
    var assistant = new ToolCallingAssistant(
        activeProvider, interactiveWorkspaceRoot, memoryStore, imageProvider, cloudSpeechProvider);
    await assistant.RunAsync();
    return;
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

IAIProvider planningProvider = activeProvider;

Console.WriteLine(
    $"Planner   : {planningProvider.Name}");
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
// Git checkpoint manager
// ------------------------------------------------------------

var gitCheckpointManager =
    new GitCheckpointManager(
        terminalController,
        workspaceRoot);

// ------------------------------------------------------------
// Development orchestrator
// ------------------------------------------------------------

var developmentOrchestrator =
    new DevelopmentOrchestrator(
        developmentLoop,
        autonomousExecutor,
        gitCheckpointManager);

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
            maxAttempts: 50);

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


