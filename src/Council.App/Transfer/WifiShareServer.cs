using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Council.App.Transfer;

/// <summary>
/// Temporary Wi-Fi file sharing between this laptop and a phone (or any device with a browser) on the
/// same network. It serves one folder only, needs a random secret in the link, never overwrites files,
/// limits uploads to 1 GB each, and stops by itself after the chosen time (30 minutes by default).
/// </summary>
public sealed class WifiShareServer : IAsyncDisposable
{
    public const long MaxUploadBytes = 1L << 30;
    private WebApplication? _app;
    private CancellationTokenSource? _autoStop;

    public string Folder { get; }
    public string? Token { get; private set; }
    public int Port { get; private set; }
    public DateTimeOffset? StopsAt { get; private set; }
    public bool Running => _app is not null;

    public WifiShareServer(string? folder = null)
    {
        Folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "JARVIS-Share");
    }

    /// <summary>Starts the server. <paramref name="loopbackOnly"/> is for tests (no firewall prompt).</summary>
    public async Task<IReadOnlyList<string>> StartAsync(TimeSpan duration, bool loopbackOnly = false, int port = 0)
    {
        if (_app is not null) return Links(loopbackOnly);
        Directory.CreateDirectory(Folder);
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = MaxUploadBytes;
            if (loopbackOnly) k.Listen(IPAddress.Loopback, port); else k.Listen(IPAddress.Any, port);
        });
        builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = MaxUploadBytes);
        var app = builder.Build();

        // Every route lives under the random secret, so anything else is a plain 404.
        var share = app.MapGroup("/" + Token);
        share.MapGet("/", () => Results.Content(Page(), "text/html; charset=utf-8"));
        share.MapGet("/file/{name}", (string name) =>
        {
            var path = SafePath(name);
            return path is null || !File.Exists(path) ? Results.NotFound() : Results.File(path, "application/octet-stream", Path.GetFileName(path));
        });
        share.MapPost("/upload", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest("Choose a file first.");
            var form = await request.ReadFormAsync();
            var saved = new List<string>();
            foreach (var file in form.Files)
            {
                var target = UniqueTarget(file.FileName);
                if (target is null) continue;
                await using (var stream = new FileStream(target, FileMode.CreateNew))
                    await file.CopyToAsync(stream);
                saved.Add(Path.GetFileName(target));
            }
            Uploaded?.Invoke(saved);
            return Results.Content(Page(saved.Count == 0 ? "Nothing was uploaded." : "Saved on the laptop: " + string.Join(", ", saved)),
                "text/html; charset=utf-8");
        }).DisableAntiforgery();

        await app.StartAsync();
        _app = app;
        Port = new Uri(app.Urls.First()).Port;
        StopsAt = DateTimeOffset.Now + duration;
        _autoStop = new CancellationTokenSource();
        var token = _autoStop.Token;
        _ = Task.Delay(duration, token).ContinueWith(async _ => await StopAsync(), token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
        return Links(loopbackOnly);
    }

    /// <summary>Raised with the names of files saved from the phone.</summary>
    public event Action<IReadOnlyList<string>>? Uploaded;

    public async Task StopAsync()
    {
        var app = _app;
        _app = null;
        _autoStop?.Cancel();
        StopsAt = null;
        if (app is not null)
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    public IReadOnlyList<string> Links(bool loopbackOnly = false)
    {
        if (_app is null) return Array.Empty<string>();
        var hosts = loopbackOnly ? new List<string> { "127.0.0.1" } : LocalAddresses();
        return hosts.Select(h => $"http://{h}:{Port}/{Token}/").ToList();
    }

    /// <summary>Copies a laptop file into the share folder so the phone can download it.</summary>
    public string Share(string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("File not found: " + source);
        var target = UniqueTarget(Path.GetFileName(source)) ?? throw new ArgumentException("That file name cannot be shared.");
        File.Copy(source, target);
        return Path.GetFileName(target);
    }

    private string? SafePath(string name)
    {
        var clean = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(clean) || clean.StartsWith('.')) return null;
        var full = Path.GetFullPath(Path.Combine(Folder, clean));
        return full.StartsWith(Path.GetFullPath(Folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private string? UniqueTarget(string fileName)
    {
        var first = SafePath(fileName);
        if (first is null) return null;
        if (!File.Exists(first)) return first;
        var stem = Path.GetFileNameWithoutExtension(first);
        var ext = Path.GetExtension(first);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(Folder, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private string Page(string? message = null)
    {
        var files = new DirectoryInfo(Folder).EnumerateFiles()
            .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden))
            .OrderByDescending(f => f.LastWriteTime).Take(200).ToList();
        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'>")
            .Append("<title>JARVIS share</title><style>body{font:16px system-ui;margin:16px;max-width:640px}li{margin:8px 0}")
            .Append("button,input{font-size:16px;padding:8px}.m{background:#e8f5e9;padding:8px}</style></head><body><h2>JARVIS file share</h2>");
        if (message is not null) html.Append("<p class=m>").Append(WebUtility.HtmlEncode(message)).Append("</p>");
        html.Append("<form method=post action='upload' enctype='multipart/form-data'><p><b>Send to laptop</b></p>")
            .Append("<input type=file name=files multiple> <button>Upload</button></form><h3>On the laptop</h3><ul>");
        if (files.Count == 0) html.Append("<li>No files yet. Ask JARVIS to share one.</li>");
        foreach (var f in files)
            html.Append("<li><a href='file/").Append(Uri.EscapeDataString(f.Name)).Append("'>").Append(WebUtility.HtmlEncode(f.Name))
                .Append("</a> (").Append(f.Length >= 1 << 20 ? $"{f.Length / (double)(1 << 20):0.0} MB" : $"{f.Length / 1024.0:0} KB").Append(")</li>");
        html.Append("</ul><p><small>Stops automatically").Append(StopsAt is { } s ? $" at {s:HH:mm}" : "").Append(".</small></p></body></html>");
        return html.ToString();
    }

    private static List<string> LocalAddresses()
    {
        var result = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;
            if (nic.Description.Contains("virtual", StringComparison.OrdinalIgnoreCase) || nic.Description.Contains("hyper-v", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var address in nic.GetIPProperties().UnicastAddresses)
                if (address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address) &&
                    !address.Address.ToString().StartsWith("169.254."))
                    result.Add(address.Address.ToString());
        }
        return result.Count == 0 ? new List<string> { "127.0.0.1" } : result;
    }
}
