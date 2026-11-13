using Council.AI.Providers;
using Council.App.Chat;
using Council.Core.Models;
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

Console.Title = "JARVIS";
Console.WriteLine("============================================================");
Console.WriteLine("                         J.A.R.V.I.S");
Console.WriteLine("             Personal AI Assistant - Windows");
Console.WriteLine("============================================================");

var workspaceRoot = ResolveWorkspaceRoot();
var historyStore = new ConversationStore();
var history = await historyStore.LoadAsync();
var provider = CreateProvider();

Console.WriteLine($"AI route  : {provider.Name}");
Console.WriteLine($"Workspace : {workspaceRoot}");
Console.WriteLine($"Memory    : {history.Count} recent chat messages restored.");

if (provider is MockAIProvider)
{
    Console.WriteLine();
    Console.WriteLine("DEMO MODE: No AI API key is configured.");
    Console.WriteLine("Set GEMINI_API_KEY, OPENAI_API_KEY, or");
    Console.WriteLine("OMNIROUTE_API_KEY + OMNIROUTE_BASE_URL to enable AI.");
}

var taskManager = new TaskManager(new TaskStore());
await taskManager.InitializeAsync();

var terminal = new TerminalController();
var workspace = new SafeWorkspaceExecutor(workspaceRoot);
var actionExecutor = new DevelopmentActionExecutor(
    workspace,
    terminal,
    workspaceRoot);
var validator = new BuildTestExecutor(terminal, workspaceRoot);
var planner = new AIDevelopmentPlanner(
    provider,
    new RepositoryContextProvider(workspaceRoot));
var autonomousExecutor = new AutonomousDevelopmentExecutor(
    planner,
    actionExecutor,
    validator,
    taskManager);
var developmentLoop = new DevelopmentLoop(
    taskManager,
    new TaskRunner(taskManager));
var checkpointManager = new GitCheckpointManager(terminal, workspaceRoot);
var developmentOrchestrator = new DevelopmentOrchestrator(
    developmentLoop,
    autonomousExecutor,
    checkpointManager);

Console.WriteLine();
Console.WriteLine("JARVIS is ready. Type /help to see commands.");
Console.WriteLine();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("You > ");
    Console.ResetColor();

    var input = Console.ReadLine();
    if (input is null)
        break;

    input = input.Trim();
    if (input.Length == 0)
        continue;

    if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase) ||
        input.Equals("/quit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
    {
        PrintHelp();
        continue;
    }

    if (input.Equals("/status", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine($"AI route       : {provider.Name}");
        Console.WriteLine($"Chat messages  : {history.Count}");
        Console.WriteLine($"Workspace      : {workspaceRoot}");
        Console.WriteLine($"Saved dev tasks: {taskManager.Tasks.Count}");
        continue;
    }

    if (input.Equals("/history", StringComparison.OrdinalIgnoreCase))
    {
        foreach (var message in history.TakeLast(12))
        {
            var role = message.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? "You"
                : "JARVIS";
            Console.WriteLine($"{role} > {message.Content}");
        }
        continue;
    }

    if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
    {
        history.Clear();
        await historyStore.ClearAsync();
        Console.WriteLine("Conversation cleared. Your development-task records are unchanged.");
        continue;
    }

    if (input.StartsWith("/dev ", StringComparison.OrdinalIgnoreCase))
    {
        await RunDevelopmentTaskAsync(input[5..].Trim());
        continue;
    }

    if (input.Equals("/dev", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Usage: /dev describe the development task");
        continue;
    }

    history.Add(new AIMessage("user", input));
    await historyStore.SaveAsync(history);

    var requestMessages = new List<AIMessage>
    {
        new("system", AssistantInstructions)
    };
    requestMessages.AddRange(history.TakeLast(40));

    try
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("JARVIS is thinking...");
        Console.ResetColor();

        var response = await provider.AskAsync(
            new AIRequest(requestMessages, Temperature: 0.35));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine();
        Console.WriteLine($"JARVIS [{response.Provider}/{response.Model}] >");
        Console.ResetColor();
        Console.WriteLine(response.Content);
        Console.WriteLine();

        history.Add(new AIMessage("assistant", response.Content));
        if (history.Count > 120)
            history.RemoveRange(0, history.Count - 120);
        await historyStore.SaveAsync(history);
    }
    catch (Exception exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"AI request failed: {exception.GetBaseException().Message}");
        Console.ResetColor();
        Console.WriteLine("Your message is still saved. Try again or configure another provider.");
    }
}

Console.WriteLine();
Console.WriteLine("JARVIS session saved. Goodbye.");

async Task RunDevelopmentTaskAsync(string description)
{
    if (string.IsNullOrWhiteSpace(description))
    {
        Console.WriteLine("Describe what you want JARVIS to build: /dev ...");
        return;
    }

    Console.WriteLine();
    Console.WriteLine("This task can create or modify files and run dotnet build/tests.");
    Console.WriteLine("A clean Git working tree is required so JARVIS can roll back safely.");
    Console.Write("Type RUN to authorize this development task: ");
    var approval = Console.ReadLine();

    if (!string.Equals(approval?.Trim(), "RUN", StringComparison.Ordinal))
    {
        Console.WriteLine("Development task cancelled.");
        return;
    }

    var title = description.Length > 72
        ? description[..72]
        : description;
    var task = await taskManager.CreateAsync(title, description);

    Console.WriteLine($"Task ID: {task.Id}");
    Console.WriteLine("JARVIS is planning, executing, validating and checkpointing the work...");

    try
    {
        var result = await developmentOrchestrator.ExecuteAsync(
            task,
            maxAttempts: 3);

        Console.WriteLine();
        Console.WriteLine(result.Success
            ? "Development task completed."
            : "Development task did not complete.");
        Console.WriteLine(result.Message);
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Development task stopped: {exception.GetBaseException().Message}");
    }
}

IAIProvider CreateProvider()
{
    var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
    var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    var omniKey = Environment.GetEnvironmentVariable("OMNIROUTE_API_KEY");
    var omniBaseUrl = Environment.GetEnvironmentVariable("OMNIROUTE_BASE_URL");
    var selection = Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER")
        ?.Trim()
        .ToLowerInvariant();

    var geminiModel = Environment.GetEnvironmentVariable("GEMINI_MODEL")
        ?? "gemini-3.6-flash";
    var openAiModel = Environment.GetEnvironmentVariable("OPENAI_MODEL")
        ?? "gpt-5.2";
    var omniModel = Environment.GetEnvironmentVariable("OMNIROUTE_MODEL")
        ?? "auto";

    var configured = new List<IAIProvider>();

    void AddGemini()
    {
        if (!string.IsNullOrWhiteSpace(geminiKey))
            configured.Add(new GeminiProvider(geminiKey, geminiModel));
    }

    void AddOpenAI()
    {
        if (!string.IsNullOrWhiteSpace(openAiKey))
            configured.Add(new OpenAIProvider(openAiKey, openAiModel));
    }

    void AddOmniRoute()
    {
        if (!string.IsNullOrWhiteSpace(omniKey) &&
            !string.IsNullOrWhiteSpace(omniBaseUrl))
        {
            configured.Add(new OpenAICompatibleProvider(
                omniBaseUrl,
                omniKey,
                omniModel,
                "OmniRoute"));
        }
    }

    switch (selection)
    {
        case "gemini":
            AddGemini();
            AddOmniRoute();
            AddOpenAI();
            break;
        case "openai":
            AddOpenAI();
            AddGemini();
            AddOmniRoute();
            break;
        case "omniroute":
        case "compatible":
            AddOmniRoute();
            AddGemini();
            AddOpenAI();
            break;
        case null or "":
            AddGemini();
            AddOmniRoute();
            AddOpenAI();
            break;
        default:
            throw new InvalidOperationException(
                $"Unknown JARVIS_AI_PROVIDER '{selection}'. Use gemini, openai or omniroute.");
    }

    return configured.Count switch
    {
        0 => new MockAIProvider(),
        1 => configured[0],
        _ => new FallbackAIProvider(configured)
    };
}

string ResolveWorkspaceRoot()
{
    var configured = Environment.GetEnvironmentVariable("JARVIS_WORKSPACE");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        var fullPath = Path.GetFullPath(configured);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"JARVIS_WORKSPACE does not exist: {fullPath}");
        return fullPath;
    }

    var directory = new DirectoryInfo(Environment.CurrentDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "JARVIS.Council.sln")))
            return directory.FullName;
        directory = directory.Parent;
    }

    return Environment.CurrentDirectory;
}

void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("JARVIS COMMANDS");
    Console.WriteLine("  /help             Show this help");
    Console.WriteLine("  /status           Show provider, memory and workspace status");
    Console.WriteLine("  /history          Show recent conversation");
    Console.WriteLine("  /clear            Clear conversation history");
    Console.WriteLine("  /dev <task>       Start an explicitly approved development task");
    Console.WriteLine("  /exit             Save and close JARVIS");
    Console.WriteLine();
    Console.WriteLine("AI provider environment variables:");
    Console.WriteLine("  GEMINI_API_KEY, OPENAI_API_KEY");
    Console.WriteLine("  OMNIROUTE_API_KEY + OMNIROUTE_BASE_URL");
    Console.WriteLine("  Optional: JARVIS_AI_PROVIDER, GEMINI_MODEL, OPENAI_MODEL, OMNIROUTE_MODEL");
    Console.WriteLine();
}

const string AssistantInstructions = """
You are JARVIS, the user's practical personal AI assistant.
Be useful, accurate, direct and transparent about uncertainty.
Use the conversation history when relevant. Do not claim to have opened apps,
changed files, searched live markets, sent messages or performed actions unless
a connected tool actually completed that action.
When asked to modify this repository, explain that the user can invoke the
explicit /dev command. Never ask for API keys in chat and never expose secrets.
Prefer concise answers, but give detailed technical explanations when requested.
""";
