using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Council.Security;

namespace Council.App.Notify;

public sealed record PhoneAlertSettings(bool Enabled, string Server, string Topic);

/// <summary>
/// Sends JARVIS alerts (reminders, focus nudges) to the user's phone through ntfy (https://ntfy.sh, free and
/// open source, no account). The phone runs the ntfy app subscribed to a random secret topic; a smartwatch that
/// mirrors phone notifications (such as the owner's Timex Fit 2.0) then shows them too. Alerts pass through the
/// ntfy server, so secrets are redacted and only short texts are sent.
/// </summary>
public sealed class PhoneAlerts
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly HttpClient _http;

    public PhoneAlerts(string? path = null, HttpClient? http = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "phone-alerts.json");
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public PhoneAlertSettings Settings()
    {
        if (!File.Exists(_path)) return new PhoneAlertSettings(false, "https://ntfy.sh", "");
        try { return JsonSerializer.Deserialize<PhoneAlertSettings>(File.ReadAllText(_path)) ?? new(false, "https://ntfy.sh", ""); }
        catch (JsonException) { return new PhoneAlertSettings(false, "https://ntfy.sh", ""); }
    }

    /// <summary>Turns alerts on, creating a random secret topic the first time.</summary>
    public PhoneAlertSettings Enable(string? server = null)
    {
        var current = Settings();
        var topic = current.Topic.Length >= 16 ? current.Topic : "jarvis-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
        var url = string.IsNullOrWhiteSpace(server) ? current.Server : server.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new ArgumentException("The ntfy server must be an https address.");
        var settings = new PhoneAlertSettings(true, url, topic);
        Save(settings);
        return settings;
    }

    public void Disable() => Save(Settings() with { Enabled = false });

    /// <summary>Sends an alert if enabled. Returns false when off; throws on network errors.</summary>
    public async Task<bool> SendAsync(string title, string message, bool urgent = false, CancellationToken token = default)
    {
        var s = Settings();
        if (!s.Enabled || s.Topic.Length == 0) return false;
        var body = SecretRedactor.Redact(message ?? "");
        if (body.Length > 500) body = body[..500] + "...";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{s.Server}/{s.Topic}")
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
        request.Headers.TryAddWithoutValidation("Title", Ascii(SecretRedactor.Redact(title ?? "JARVIS")));
        request.Headers.TryAddWithoutValidation("Priority", urgent ? "high" : "default");
        request.Headers.TryAddWithoutValidation("Tags", "robot");
        using var response = await _http.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>HTTP headers must be ASCII; non-ASCII titles (Telugu, Hindi) fall back to "JARVIS".</summary>
    private static string Ascii(string text)
    {
        var clean = new string(text.Where(c => c is >= ' ' and <= '~').ToArray()).Trim();
        return clean.Length == 0 ? "JARVIS" : clean.Length > 80 ? clean[..80] : clean;
    }

    private void Save(PhoneAlertSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
