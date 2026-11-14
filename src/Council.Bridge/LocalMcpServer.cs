using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Council.App;

namespace Council.Bridge;

/// <summary>
/// Stateless MCP-over-HTTP endpoint for local development assistance.
/// It binds to loopback only and exposes fixed, non-shell tools.
/// </summary>
public sealed class LocalMcpServer : IAsyncDisposable
{
    private const int MaxRequestBytes = 1024 * 1024;
    private const int MaxOutputCharacters = 24_000;
    private readonly HttpListener _listener = new();
    private readonly byte[] _token;
    private readonly WorkspacePathGuard _workspace;
    private readonly DevelopmentTaskQueueService _tasks;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<int, Task> _handlers = new();
    private Task? _acceptLoop;
    private int _handlerId;

    public LocalMcpServer(string workspaceRoot, string bearerToken, int port = 47822)
    {
        if (string.IsNullOrWhiteSpace(bearerToken) || Encoding.UTF8.GetByteCount(bearerToken) < 32)
            throw new ArgumentException("The bridge token must contain at least 32 UTF-8 bytes.", nameof(bearerToken));
        if (port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        _workspace = new WorkspacePathGuard(workspaceRoot);
        _tasks = new DevelopmentTaskQueueService();
        _token = Encoding.UTF8.GetBytes(bearerToken);
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        Port = port;
    }

    public int Port { get; }

    public void Start()
    {
        if (_acceptLoop is not null)
            throw new InvalidOperationException("The development bridge is already running.");

        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_stop.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var id = Interlocked.Increment(ref _handlerId);
            var task = HandleAsync(context, cancellationToken);
            _handlers[id] = task;
            _ = task.ContinueWith(
                _ => _handlers.TryRemove(id, out _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!IsAuthorized(context.Request.Headers["Authorization"]))
            {
                await WriteJsonAsync(context.Response, 401, new { error = "Unauthorized" }, cancellationToken);
                return;
            }

            var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            if (context.Request.HttpMethod == "GET" && path == "/healthz")
            {
                await WriteJsonAsync(context.Response, 200, new
                {
                    status = "online",
                    transport = "MCP over HTTP",
                    bind = "127.0.0.1",
                    workspace = _workspace.Root
                }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod != "POST" || path != "/mcp")
            {
                await WriteJsonAsync(context.Response, 404, new { error = "Not found" }, cancellationToken);
                return;
            }

            if (context.Request.ContentLength64 > MaxRequestBytes)
            {
                await WriteJsonAsync(context.Response, 413, new { error = "Request body is too large." }, cancellationToken);
                return;
            }

            if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, 415, new { error = "Content-Type must be application/json." }, cancellationToken);
                return;
            }

            using var document = await JsonDocument.ParseAsync(
                context.Request.InputStream,
                new JsonDocumentOptions { MaxDepth = 32 },
                cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("method", out var methodNode) ||
                methodNode.ValueKind != JsonValueKind.String)
            {
                await WriteJsonAsync(context.Response, 400, new { error = "Invalid JSON-RPC request." }, cancellationToken);
                return;
            }

            var method = methodNode.GetString()!;
            var hasId = root.TryGetProperty("id", out var idNode);
            var id = hasId ? idNode.Clone() : (JsonElement?)null;

            if (!hasId || method == "notifications/initialized")
            {
                context.Response.StatusCode = 202;
                context.Response.Close();
                return;
            }

            var parameters = root.TryGetProperty("params", out var paramsNode)
                ? paramsNode
                : default;

            object result = method switch
            {
                "initialize" => new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "jarvis-local-development-bridge", version = "0.1.0" },
                    instructions = "Loopback-only JARVIS development tools. File access is read-only. Build and test commands are fixed allowlisted operations."
                },
                "ping" => new { },
                "tools/list" => new { tools = GetTools() },
                "tools/call" => await CallToolAsync(parameters, cancellationToken),
                _ => throw new McpMethodNotFoundException(method)
            };

            await WriteJsonAsync(context.Response, 200, new { jsonrpc = "2.0", id, result }, cancellationToken);
        }
        catch (McpMethodNotFoundException ex)
        {
            await WriteJsonAsync(context.Response, 200, new
            {
                jsonrpc = "2.0",
                id = GetRequestId(context),
                error = new { code = -32601, message = ex.Message }
            }, CancellationToken.None);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(context.Response, 400, new { error = "Malformed JSON-RPC request." }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            try
            {
                await WriteJsonAsync(context.Response, 500, new { error = "Bridge request failed: " + SafeMessage(ex) }, CancellationToken.None);
            }
            catch
            {
                context.Response.Abort();
            }
        }
    }

    private async Task<object> CallToolAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameNode) ||
            nameNode.ValueKind != JsonValueKind.String)
            return ToolError("tools/call requires a tool name.");

        var name = nameNode.GetString()!;
        var arguments = parameters.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object
            ? args
            : default;

        try
        {
            string output = name switch
            {
                "workspace_list" => JsonSerializer.Serialize(_workspace.ListEntries(OptionalString(arguments, "path"))),
                "workspace_read_file" => _workspace.ReadTextFile(RequiredString(arguments, "path")),
                "git_status" => await RunProcessAsync("git", ["status", "--short", "--branch"], cancellationToken),
                "dotnet_restore" => await RunProcessAsync("dotnet", ["restore", "JARVIS.Council.sln"], cancellationToken),
                "dotnet_build" => await RunProcessAsync("dotnet", ["build", "JARVIS.Council.sln", "--configuration", "Release"], cancellationToken),
                "dotnet_test" => await RunProcessAsync("dotnet", ["test", "JARVIS.Council.sln", "--configuration", "Release"], cancellationToken),
                "development_task_create" => await CreateDevelopmentTaskAsync(arguments, cancellationToken),
                _ => throw new ArgumentException("Unknown tool: " + name)
            };

            return ToolSuccess(output);
        }
        catch (Exception ex)
        {
            return ToolError(SafeMessage(ex));
        }
    }

    private async Task<string> CreateDevelopmentTaskAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var title = RequiredString(arguments, "title");
        var description = RequiredString(arguments, "description");
        var task = await _tasks.CreateAsync(title, description, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            task.Id,
            task.Title,
            status = task.Status.ToString(),
            task.IsApproved,
            message = "Task queued. It has not been approved or executed. Review it in the existing JARVIS Development Tasks window."
        });
    }

    private async Task<string> RunProcessAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = _workspace.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Could not start the allowlisted command: " + executable);

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (cancellationToken.IsCancellationRequested)
                throw;
            return executable + " timed out after 10 minutes.";
        }

        var standardOutput = await stdout;
        var standardError = await stderr;
        var combined = $"Exit code: {process.ExitCode}{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}";
        if (combined.Length > MaxOutputCharacters)
            combined = combined[..MaxOutputCharacters] + Environment.NewLine + "[Output truncated by JARVIS bridge.]";
        return combined;
    }

    private static object[] GetTools() =>
    [
        Tool("workspace_list", "List files and directories immediately inside a workspace directory. Hidden build and Git folders are omitted.",
            new { path = new { type = "string", description = "Optional workspace-relative directory; defaults to the repository root." } }),
        Tool("workspace_read_file", "Read a small text source/documentation file inside the repository. Absolute paths, traversal, links, secrets and unsupported file types are blocked.",
            new { path = new { type = "string", description = "Workspace-relative file path." } }, "path"),
        Tool("git_status", "Read Git working-tree status. No Git write operations are exposed."),
        Tool("dotnet_restore", "Run the fixed dotnet restore command for JARVIS.Council.sln."),
        Tool("dotnet_build", "Run the fixed Release build for JARVIS.Council.sln."),
        Tool("dotnet_test", "Run the fixed Release test suite for JARVIS.Council.sln."),
        Tool("development_task_create", "Create a task in JARVIS's existing persistent development queue. Creation does not approve or execute it.",
            new
            {
                title = new { type = "string", description = "Short task title." },
                description = new { type = "string", description = "Task scope and acceptance criteria." }
            }, "title", "description")
    ];

    private static object Tool(string name, string description, object? properties = null, params string[] required)
    {
        var schema = properties is null
            ? new { type = "object", properties = new Dictionary<string, object>(), required = Array.Empty<string>(), additionalProperties = false }
            : new { type = "object", properties, required, additionalProperties = false };
        return new { name, description, inputSchema = schema };
    }

    private static object ToolSuccess(string text) =>
        new { content = new[] { new { type = "text", text } }, isError = false };

    private static object ToolError(string text) =>
        new { content = new[] { new { type = "text", text } }, isError = true };

    private static string RequiredString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"A non-empty '{name}' argument is required.");
        return value.GetString()!.Trim();
    }

    private static string OptionalString(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object &&
        arguments.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private bool IsAuthorized(string? header)
    {
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        var supplied = Encoding.UTF8.GetBytes(header[7..].Trim());
        return supplied.Length == _token.Length && CryptographicOperations.FixedTimeEquals(supplied, _token);
    }

    private static JsonElement? GetRequestId(HttpListenerContext context)
    {
        // Error responses for malformed/unknown methods need not echo an untrusted ID.
        return null;
    }

    private static string SafeMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Length <= 1000 ? message : message[..1000];
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object payload, CancellationToken cancellationToken)
    {
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_listener.IsListening)
            _listener.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch (HttpListenerException) { }
        }

        var handlers = _handlers.Values.ToArray();
        if (handlers.Length > 0)
        {
            try { await Task.WhenAll(handlers); } catch { }
        }

        _listener.Close();
        _stop.Dispose();
        CryptographicOperations.ZeroMemory(_token);
    }

    private sealed class McpMethodNotFoundException(string method)
        : Exception("Method not found: " + method);
}
