using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;
using Council.Execution;
using Council.Execution.Tools;

namespace Council.App;

public sealed class ToolCallingAssistant
{
    private const int MaxModelRoundsPerTurn = 4;
    private const int MaxToolExecutionsPerTurn = 8;
    private const int MaxHistoryMessages = 25;
    private readonly IAIProvider _provider;
    private readonly ToolRegistry _tools = new();
    private readonly List<AIMessage> _history = [];

    private const string SystemPrompt = """
        You are JARVIS, a practical personal AI assistant.
        You have limited tools that can access ONLY the configured JARVIS project workspace.
        Never claim an action succeeded unless its tool result confirms success.
        Never invent file contents or search results.
        When a tool is needed, respond with exactly one JSON object and no markdown.
        For one operation use {"type":"tool_call","name":"read_file","arguments":{"path":"relative/path"}}.
        For multiple operations, batch them in one response: {"type":"tool_calls","calls":[{"name":"read_file","arguments":{"path":"a.txt"}},{"name":"read_file","arguments":{"path":"b.txt"}}]}.
        All argument values must be strings. Include only required arguments. list_files recursively lists all visible descendants, so use it ONCE with an empty path to list the whole workspace.
        After receiving tool results, either call another tool or respond:
        {"type":"message","content":"your answer"}
        If no tool is needed, respond using the message format.
        Do not request or access secrets, credentials, .env files, appsettings files, .git, bin, obj, node_modules, or files outside the workspace.
        Do not overwrite existing files. Do not delete files. Do not run shell commands.
        Be clear and concise. Explain when a requested action is blocked.
        """;

    public ToolCallingAssistant(IAIProvider provider, string workspaceRoot)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _tools.Register(new WorkspaceTool("list_files", "Recursively list visible files and folders. Required argument: path (use empty string for workspace root).", workspaceRoot));
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
                var toolExecutions = 0;
                for (var round = 0; round < MaxModelRoundsPerTurn; round++)
                {
                    var response = await _provider.AskAsync(new AIRequest(_history.ToArray(), Temperature: 0.2), cancellationToken);
                    if (string.IsNullOrWhiteSpace(response.Content))
                    {
                        Console.WriteLine("JARVIS: The AI provider returned an empty response. Please try again." + Environment.NewLine);
                        completed = true;
                        break;
                    }
                    if (!TryParseEnvelope(response.Content, out var type, out var calls, out var message))
                    {
                        var cleanText = response.Content.Trim();
                        Console.WriteLine("JARVIS: " + cleanText + Environment.NewLine);
                        _history.Add(new AIMessage("assistant", cleanText));
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
                    if (calls.Count == 0 || toolExecutions + calls.Count > MaxToolExecutionsPerTurn)
                    {
                        Console.WriteLine("JARVIS: This request needs too many workspace operations. Please split it into smaller steps." + Environment.NewLine);
                        completed = true;
                        break;
                    }
                    _history.Add(new AIMessage("assistant", response.Content));
                    foreach (var call in calls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!_tools.TryGet(call.Name, out var tool))
                        {
                            Console.WriteLine($"[Tool: {call.Name}] Blocked/failed");
                            _history.Add(new AIMessage("user", "Tool result: FAILED: Unknown tool name; operation blocked."));
                        }
                        else
                        {
                            var result = await tool.ExecuteAsync(call.Arguments, cancellationToken);
                            Console.WriteLine($"[Tool: {tool.Name}] {(result.Success ? "Success" : "Blocked/failed")}");
                            _history.Add(new AIMessage("user", "Tool result (" + tool.Name + "): " + (result.Success ? "SUCCESS: " : "FAILED: ") + result.Message));
                        }
                        toolExecutions++;
                        TrimHistory();
                    }
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

    private sealed record ToolCall(string Name, Dictionary<string, string> Arguments);

    private static bool TryParseEnvelope(string content, out string type, out List<ToolCall> calls, out string message)
    {
        type = message = string.Empty;
        calls = [];
        var firstBrace = content.IndexOf('{');
        var lastBrace = content.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace) return false;
        try
        {
            using var doc = JsonDocument.Parse(content[firstBrace..(lastBrace + 1)]);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeNode) ||
                typeNode.ValueKind != JsonValueKind.String) return false;
            type = typeNode.GetString() ?? "";
            if (type == "message")
            {
                if (!root.TryGetProperty("content", out var contentNode) ||
                    contentNode.ValueKind != JsonValueKind.String) return false;
                message = contentNode.GetString() ?? "";
                return true;
            }
            if (type == "tool_call")
            {
                if (!TryReadToolCall(root, out var call)) return false;
                calls.Add(call);
                return true;
            }
            if (type == "tool_calls")
            {
                if (!root.TryGetProperty("calls", out var array) ||
                    array.ValueKind != JsonValueKind.Array ||
                    array.GetArrayLength() < 1 ||
                    array.GetArrayLength() > MaxToolExecutionsPerTurn) return false;
                foreach (var item in array.EnumerateArray())
                {
                    if (!TryReadToolCall(item, out var call)) return false;
                    calls.Add(call);
                }
                return true;
            }
            return false;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryReadToolCall(JsonElement node, out ToolCall call)
    {
        call = new ToolCall("", new Dictionary<string, string>());
        if (node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("name", out var nameNode) ||
            nameNode.ValueKind != JsonValueKind.String ||
            !node.TryGetProperty("arguments", out var argsNode) ||
            argsNode.ValueKind != JsonValueKind.Object) return false;
        var name = nameNode.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(name)) return false;
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in argsNode.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String ||
                !arguments.TryAdd(property.Name, property.Value.GetString() ?? "")) return false;
        }
        call = new ToolCall(name, arguments);
        return true;
    }

    private void TrimHistory()
    {
        while (_history.Count > MaxHistoryMessages + 1)
            _history.RemoveAt(1);
    }
}
