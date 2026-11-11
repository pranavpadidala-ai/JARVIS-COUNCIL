using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;
using Council.Execution;
using Council.Execution.Tools;

namespace Council.App;

public sealed class ToolCallingAssistant
{
    private const int MaxToolCallsPerTurn = 3;
    private const int MaxHistoryMessages = 25;
    private readonly IAIProvider _provider;
    private readonly ToolRegistry _tools = new();
    private readonly List<AIMessage> _history = [];

    private const string SystemPrompt = """
        You are JARVIS, a practical personal AI assistant.
        You have limited tools that can access ONLY the configured JARVIS project workspace.
        Never claim an action succeeded unless its tool result confirms success.
        Never invent file contents or search results.
        When a tool is needed, respond with exactly one JSON object and no markdown:
        {"type":"tool_call","name":"list_files|read_file|search_files|create_file|create_folder","arguments":{"path":"relative/path","query":"text","content":"text"}}
        Include only arguments required by the selected tool. All argument values must be strings.
        After receiving a tool result, either call another tool using the same format or respond:
        {"type":"message","content":"your answer"}
        If no tool is needed, respond using the message format.
        Do not request or access secrets, credentials, .env files, appsettings files, .git, bin, obj, node_modules, or files outside the workspace.
        Do not overwrite existing files. Do not delete files. Do not run shell commands.
        Be clear and concise. Explain when a requested action is blocked.
        """;

    public ToolCallingAssistant(IAIProvider provider, string workspaceRoot)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _tools.Register(new WorkspaceTool("list_files", "List files and folders in a workspace folder. Required argument: path (use empty string for root).", workspaceRoot));
        _tools.Register(new WorkspaceTool("read_file", "Read a text file. Required argument: path.", workspaceRoot));
        _tools.Register(new WorkspaceTool("search_files", "Search supported project text files. Required argument: query.", workspaceRoot));
        _tools.Register(new WorkspaceTool("create_file", "Create a new file without overwriting. Required arguments: path, content. Parent folder must exist.", workspaceRoot));
        _tools.Register(new WorkspaceTool("create_folder", "Create a folder in the workspace. Required argument: path.", workspaceRoot));
        _history.Add(new AIMessage("system", SystemPrompt + "\n\nAvailable tools:\n" +
            string.Join("\n", _tools.GetAll().Select(t => t.Name + ": " + t.Description))));
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("                         J.A.R.V.I.S");
        Console.WriteLine("============================================================");
        Console.WriteLine($"Provider : {_provider.Name}");
        Console.WriteLine("Workspace: " + Path.GetFullPath(Directory.GetCurrentDirectory()));
        Console.WriteLine("Tools    : list_files, read_file, search_files, create_file, create_folder");
        Console.WriteLine("Commands : /help, /clear, /exit");
        Console.WriteLine();

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("You: ");
            var input = Console.ReadLine();
            if (input is null) break;
            input = input.Trim();
            if (input.Length == 0) continue;
            if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase)) break;
            if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Ask JARVIS to inspect, search, or create files inside this project. /clear resets this session; /exit closes it.");
                continue;
            }
            if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                _history.RemoveRange(1, _history.Count - 1);
                Console.WriteLine("Conversation cleared.");
                continue;
            }

            _history.Add(new AIMessage("user", input));
            TrimHistory();
            Console.WriteLine();
            try
            {
                var completed = false;
                for (var call = 0; call <= MaxToolCallsPerTurn; call++)
                {
                    var response = await _provider.AskAsync(new AIRequest(_history.ToArray(), Temperature: 0.2), cancellationToken);
                    if (!TryParseEnvelope(response.Content, out var type, out var name, out var arguments, out var message))
                    {
                        Console.WriteLine("JARVIS: " + response.Content + Environment.NewLine);
                        _history.Add(new AIMessage("assistant", response.Content));
                        completed = true;
                        break;
                    }

                    if (type == "message")
                    {
                        Console.WriteLine("JARVIS: " + message + Environment.NewLine);
                        _history.Add(new AIMessage("assistant", message));
                        completed = true;
                        break;
                    }

                    if (type != "tool_call")
                    {
                        Console.WriteLine("JARVIS: I received an unsupported response format." + Environment.NewLine);
                        completed = true;
                        break;
                    }

                    if (call == MaxToolCallsPerTurn)
                    {
                        Console.WriteLine("JARVIS: I reached the tool-call limit for this request. Please split the task into smaller steps." + Environment.NewLine);
                        completed = true;
                        break;
                    }

                    _history.Add(new AIMessage("assistant", response.Content));
                    if (!_tools.TryGet(name, out var tool))
                    {
                        _history.Add(new AIMessage("user", "Tool result: blocked. Unknown tool name."));
                        continue;
                    }

                    var result = await tool.ExecuteAsync(arguments, cancellationToken);
                    Console.WriteLine($"[Tool: {tool.Name}] {(result.Success ? "Success" : "Blocked/failed")}");
                    _history.Add(new AIMessage("user", "Tool result (" + tool.Name + "): " + (result.Success ? "SUCCESS: " : "FAILED: ") + result.Message));
                    TrimHistory();
                }
                if (!completed)
                    Console.WriteLine("JARVIS: Request ended without a final response.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("JARVIS session cancelled.");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine("JARVIS couldn't complete that request: " + ex.Message);
            }
            Console.WriteLine();
        }
        Console.WriteLine();
        Console.WriteLine("JARVIS session ended.");
    }

    private static bool TryParseEnvelope(string content, out string type, out string name,
        out Dictionary<string, string> arguments, out string message)
    {
        type = name = message = string.Empty;
        arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeNode) ||
                typeNode.ValueKind != JsonValueKind.String) return false;
            type = typeNode.GetString() ?? "";
            if (type == "message")
            {
                if (!root.TryGetProperty("content", out var contentNode) || contentNode.ValueKind != JsonValueKind.String) return false;
                message = contentNode.GetString() ?? "";
                return true;
            }
            if (type != "tool_call" || !root.TryGetProperty("name", out var nameNode) ||
                nameNode.ValueKind != JsonValueKind.String || !root.TryGetProperty("arguments", out var argsNode) ||
                argsNode.ValueKind != JsonValueKind.Object) return false;
            name = nameNode.GetString() ?? "";
            foreach (var property in argsNode.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) return false;
                arguments[property.Name] = property.Value.GetString() ?? "";
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private void TrimHistory()
    {
        while (_history.Count > MaxHistoryMessages + 1)
            _history.RemoveAt(1);
    }
}
