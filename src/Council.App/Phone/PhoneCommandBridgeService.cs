using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Council.App.Phone;

/// <summary>
/// Optional command inbox endpoint. It accepts queued text only; it never executes phone commands.
/// Bind only to a Tailscale IPv4 address so traffic stays inside the private VPN.
/// </summary>
public sealed class PhoneCommandBridgeService : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly PhoneCommandInbox _inbox;
    private readonly byte[] _tokenBytes;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public PhoneCommandBridgeService(string bindAddress, int port, string bearerToken, PhoneCommandInbox? inbox = null)
    {
        if (!IPAddress.TryParse(bindAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork || !IsTailscaleAddress(address))
            throw new ArgumentException("The bridge must bind to a Tailscale IPv4 address in 100.64.0.0/10.", nameof(bindAddress));
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(bearerToken) || Encoding.UTF8.GetByteCount(bearerToken) < 32)
            throw new ArgumentException("Bridge bearer token must be at least 32 UTF-8 bytes.", nameof(bearerToken));

        _inbox = inbox ?? new PhoneCommandInbox();
        _tokenBytes = Encoding.UTF8.GetBytes(bearerToken);
        _listener.Prefixes.Add($"http://{address}:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        _loop = RunAsync(_stop.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }

            _ = HandleAsync(context, cancellationToken);
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
            if (context.Request.HttpMethod == "GET" && path == "/api/health")
            {
                await WriteJsonAsync(context.Response, 200, new { status = "online", accepts = "queued text only", execution = false }, cancellationToken);
                return;
            }

            if (context.Request.HttpMethod == "POST" && path == "/api/commands")
            {
                if (context.Request.ContentLength64 > 8192)
                {
                    await WriteJsonAsync(context.Response, 413, new { error = "Request body is too large." }, cancellationToken);
                    return;
                }
                if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonAsync(context.Response, 415, new { error = "Content-Type must be application/json." }, cancellationToken);
                    return;
                }
                using var document = await JsonDocument.ParseAsync(context.Request.InputStream, cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("requestId", out var requestIdNode) || requestIdNode.ValueKind != JsonValueKind.String ||
                    !root.TryGetProperty("text", out var textNode) || textNode.ValueKind != JsonValueKind.String)
                {
                    await WriteJsonAsync(context.Response, 400, new { error = "Expected JSON string fields requestId and text." }, cancellationToken);
                    return;
                }

                try
                {
                    var command = await _inbox.EnqueueAsync(requestIdNode.GetString()!, textNode.GetString()!, cancellationToken);
                    await WriteJsonAsync(context.Response, 202, command, cancellationToken);
                }
                catch (ArgumentException ex)
                {
                    await WriteJsonAsync(context.Response, 400, new { error = ex.Message }, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    await WriteJsonAsync(context.Response, 429, new { error = ex.Message }, cancellationToken);
                }
                return;
            }

            if (context.Request.HttpMethod == "GET" && path == "/api/commands")
            {
                var commands = await _inbox.ListAsync(cancellationToken);
                await WriteJsonAsync(context.Response, 200, commands, cancellationToken);
                return;
            }

            const string cancelPrefix = "/api/commands/";
            if (context.Request.HttpMethod == "POST" && path.StartsWith(cancelPrefix, StringComparison.Ordinal))
            {
                var suffix = path[cancelPrefix.Length..];
                if (suffix.EndsWith("/cancel", StringComparison.Ordinal) &&
                    Guid.TryParse(suffix[..^7], out var id))
                {
                    var cancelled = await _inbox.CancelAsync(id, cancellationToken);
                    await WriteJsonAsync(context.Response, cancelled ? 200 : 404, new { cancelled }, cancellationToken);
                    return;
                }
            }

            await WriteJsonAsync(context.Response, 404, new { error = "Not found" }, cancellationToken);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(context.Response, 400, new { error = "Malformed JSON" }, CancellationToken.None);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            context.Response.Abort();
        }
        catch
        {
            try { await WriteJsonAsync(context.Response, 500, new { error = "Bridge request failed" }, CancellationToken.None); }
            catch { context.Response.Abort(); }
        }
    }

    private bool IsAuthorized(string? header)
    {
        if (header is null || !header.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var supplied = Encoding.UTF8.GetBytes(header[7..]);
        return supplied.Length == _tokenBytes.Length && CryptographicOperations.FixedTimeEquals(supplied, _tokenBytes);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object payload, CancellationToken cancellationToken)
    {
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } });
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.Close();
    }

    private static bool IsTailscaleAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_listener.IsListening) _listener.Stop();
        if (_loop is not null)
        {
            try { await _loop; } catch (HttpListenerException) { }
        }
        _listener.Close();
        _stop.Dispose();
        CryptographicOperations.ZeroMemory(_tokenBytes);
    }
}
