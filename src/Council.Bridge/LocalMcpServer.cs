using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Council.App;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace Council.Bridge;

/// <summary>
/// Stateless MCP-over-HTTP endpoint for local development assistance.
/// Kestrel binds strictly to loopback; no HTTP.sys URL reservation or admin rights are required.
/// </summary>
public sealed class LocalMcpServer : IAsyncDisposable
{
    private const int MaxRequestBytes = 1024 * 1024;
    private const int MaxOutputCharacters = 24_000;
    private readonly byte[] _token;
    private readonly WorkspacePathGuard _workspace;
    private readonly DevelopmentTaskQueueService _tasks;
    private readonly HttpClient _ollamaHttp;
    private readonly WebApplication _app;
    private bool _started;

    public LocalMcpServer(string workspaceRoot, string bearerToken, int port = 47822)
    {
        if (string.IsNullOrWhiteSpace(bearerToken) || Encoding.UTF8.GetByteCount(bearerToken) < 32)
            throw new ArgumentException("The bridge token must contain at least 32 UTF-8 bytes.", nameof(bearerToken));
        if (port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        _workspace = new WorkspacePathGuard(workspaceRoot);
        _tasks = new DevelopmentTaskQueueService();
        _ollamaHttp = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:11434/"),
            Timeout = TimeSpan.FromMinutes(10)
        };
        _token = Encoding.UTF8.GetBytes(bearerToken);
        Port = port;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ContentRootPath = _workspace.Root
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.WebHost.ConfigureKestrel(options =>
            options.Limits.MaxRequestBodySize = MaxRequestBytes);

        _app = builder.Build();
        _app.MapGet("/healthz", HandleHealthAsync);
        _app.MapPost("/mcp", HandleMcpAsync);
        _app.MapMethods("/mcp", ["GET", "PUT", "DELETE"], () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
    }

    public int Port { get; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
            throw new InvalidOperationException("The development bridge is already running.");
        await _app.StartAsync(cancellationToken);
        _started = true;
    }

    public Task WaitForShutdownAsync(CancellationToken cancellationToken = default) =>
        _app.WaitForShutdownAsync(cancellationToken);

    private Task<IResult> HandleHealthAsync(HttpContext context)
    {
        if (!IsAuthorized(context.Request.Headers["Authorization"].ToString()))
            return Task.FromResult<IResult>(Results.Unauthorized());

        return Task.FromResult(Results.Json(new
        {
            status = "online",
            transport = "MCP over HTTP",
            bind = "127.0.0.1",
            workspace = _workspace.Root
        }));
    }

    private async Task<IResult> HandleMcpAsync(HttpContext context)
    {
        if (!IsAuthorized(context.Request.Headers["Authorization"].ToString()))
            return Results.Unauthorized();

        if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { error = "Content-Type must be application/json." }, statusCode: StatusCodes.Status415UnsupportedMediaType);

        var acceptsJson = context.Request.Headers["Accept"].Any(value =>
            value?.Split(',').Any(item => item.Trim().StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) == true);
        var acceptsEventStream = context.Request.Headers["Accept"].Any(value =>
            value?.Split(',').Any(item => item.Trim().StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase)) == true);
        if (!acceptsJson || !acceptsEventStream)
            return Results.Json(new { error = "Accept must include application/json and text/event-stream." }, statusCode: StatusCodes.Status406NotAcceptable);

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(
                context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 32 },
                context.RequestAborted);
        }
        catch (JsonException)
        {
            return Results.Json(new { error = "Malformed JSON-RPC request." }, statusCode: StatusCodes.Status400BadRequest);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("jsonrpc", out var versionNode) ||
                versionNode.ValueKind != JsonValueKind.String ||
                versionNode.GetString() != "2.0" ||
                !root.TryGetProperty("method", out var methodNode) ||
                methodNode.ValueKind != JsonValueKind.String)
                return Results.Json(new { error = "Invalid JSON-RPC 2.0 request." }, statusCode: StatusCodes.Status400BadRequest);

            var method = methodNode.GetString()!;
            var hasId = root.TryGetProperty("id", out var idNode);
            var id = hasId ? idNode.Clone() : (JsonElement?)null;

            if (!hasId)
            {
                if (method.StartsWith("notifications/", StringComparison.Ordinal))
                    return Results.StatusCode(StatusCodes.Status202Accepted);

                return Results.Json(new
                {
                    jsonrpc = "2.0",
                    id = (string?)null,
                    error = new { code = -32600, message = "A JSON-RPC request must include an id." }
                }, statusCode: StatusCodes.Status400BadRequest);
            }

            var parameters = root.TryGetProperty("params", out var paramsNode)
                ? paramsNode
                : default;

            try
            {
                object result = method switch
                {
                    "initialize" => new
                    {
                        protocolVersion = NegotiateProtocolVersion(parameters),
                        capabilities = new { tools = new { listChanged = false } },
                        serverInfo = new { name = "jarvis-local-development-bridge", version = "0.1.0" },
                        instructions = "Loopback-only JARVIS development tools. File access is read-only. Build and test commands are fixed allowlisted operations."
                    },
                    "ping" => new { },
                    "tools/list" => new { tools = GetTools() },
                    "tools/call" => await CallToolAsync(parameters, context.RequestAborted),
                    _ => throw new McpMethodNotFoundException(method)
                };

                return Results.Json(new { jsonrpc = "2.0", id, result });
            }
            catch (McpMethodNotFoundException ex)
            {
                return Results.Json(new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32601, message = ex.Message }
                });
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
                "ollama_status" => await GetOllamaStatusAsync(cancellationToken),
                "ollama_chat" => await ChatWithOllamaAsync(arguments, cancellationToken),
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

    private async Task<string> GetOllamaStatusAsync(CancellationToken cancellationToken)
    {
        var models = await GetOllamaModelsAsync(cancellationToken);
        return JsonSerializer.Serialize(new
        {
            status = "online",
            endpoint = "http://127.0.0.1:11434",
            models
        });
    }

    private async Task<string> ChatWithOllamaAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var model = RequiredString(arguments, "model");
        var prompt = RequiredString(arguments, "prompt");
        if (prompt.Length > 16_000)
            throw new ArgumentException("Prompt exceeds the 16,000-character bridge limit.");

        var installedModels = await GetOllamaModelsAsync(cancellationToken);
        if (!installedModels.Contains(model, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("The requested model is not installed in the local Ollama instance. Use ollama_status to list installed models.");

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                model,
                messages = new[] { new { role = "user", content = prompt } },
                stream = false
            }), Encoding.UTF8, "application/json")
        };

        using var response = await _ollamaHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        using var document = await ReadJsonResponseAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("Ollama returned an unexpected chat response.");

        var answer = content.GetString() ?? string.Empty;
        if (answer.Length > MaxOutputCharacters)
            answer = answer[..MaxOutputCharacters] + "\\n[Response truncated by JARVIS bridge.]";
        return $"Model: {model}{Environment.NewLine}{answer}";
    }

    private async Task<IReadOnlyList<string>> GetOllamaModelsAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/tags");
        using var response = await _ollamaHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        using var document = await ReadJsonResponseAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("models", out var modelsNode) ||
            modelsNode.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Ollama returned an unexpected model-list response.");

        return modelsNode.EnumerateArray()
            .Where(item => item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("name").GetString()!)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
    }

    private static async Task<JsonDocument> ReadJsonResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
        {
            if (buffer.Length + read > 2 * 1024 * 1024)
                throw new InvalidOperationException("Ollama response exceeds the 2 MB bridge limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        if (!response.IsSuccessStatusCode)
        {
            using var error = await JsonDocument.ParseAsync(buffer, cancellationToken: cancellationToken);
            var detail = error.RootElement.ToString();
            throw new InvalidOperationException($"Ollama returned HTTP {(int)response.StatusCode}: {detail[..Math.Min(detail.Length, 1000)]}");
        }

        return await JsonDocument.ParseAsync(buffer, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
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

        var stdout = ReadBoundedAsync(process.StandardOutput, MaxOutputCharacters, cancellationToken);
        var stderr = ReadBoundedAsync(process.StandardError, MaxOutputCharacters, cancellationToken);
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

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maxCharacters, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var output = new StringBuilder(Math.Min(maxCharacters, 4096));
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            var remaining = maxCharacters - output.Length;
            if (remaining > 0)
                output.Append(buffer, 0, Math.Min(read, remaining));
            if (read > remaining)
                truncated = true;
        }

        if (truncated)
            output.Append("\\n[Stream truncated by JARVIS bridge.]");
        return output.ToString();
    }

    private static string NegotiateProtocolVersion(JsonElement parameters)
    {
        // This implementation currently supports the stable 2024-11-05 protocol.
        // MCP initialization requires the server to return a version it implements.
        return "2024-11-05";
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
            }, "title", "description"),
        Tool("ollama_status", "Check the local Ollama service and list installed model names. Connects only to 127.0.0.1:11434."),
        Tool("ollama_chat", "Send a prompt to an installed local Ollama model and return its response. No cloud API is used; model must appear in ollama_status.",
            new
            {
                model = new { type = "string", description = "Exact installed Ollama model name, for example qwen3.5:latest." },
                prompt = new { type = "string", description = "Prompt text, up to 16,000 characters." }
            }, "model", "prompt")
    ];

    private static object Tool(string name, string description, object? properties = null, params string[] required)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties ?? new Dictionary<string, object>(),
            ["required"] = required,
            ["additionalProperties"] = false
        };
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

    private static string SafeMessage(Exception exception)
    {
        var message = exception.GetBaseException().Message;
        return message.Length <= 1000 ? message : message[..1000];
    }

    public async ValueTask DisposeAsync()
    {
        if (_started)
            await _app.StopAsync();
        await _app.DisposeAsync();
        _ollamaHttp.Dispose();
        CryptographicOperations.ZeroMemory(_token);
    }

    private sealed class McpMethodNotFoundException(string method)
        : Exception("Method not found: " + method)
    {
    }
}
