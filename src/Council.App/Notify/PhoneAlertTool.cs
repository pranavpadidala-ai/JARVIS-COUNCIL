using Council.Execution.Tools;

namespace Council.App.Notify;

/// <summary>Chat control for alerts on the user's phone and smartwatch.</summary>
public sealed class PhoneAlertTool : ITool
{
    private readonly PhoneAlerts _alerts;

    public PhoneAlertTool(PhoneAlerts? alerts = null) => _alerts = alerts ?? new PhoneAlerts();

    public string Name => "phone_alerts";

    public string Description =>
        "Send JARVIS alerts to the user's phone and smartwatch (the watch mirrors phone notifications). " +
        "Arguments: action = setup | send | off | status. setup: turns alerts on and returns the steps to subscribe in the free ntfy app. " +
        "send: message (and optional title, urgent=true) to send now. Once on, reminders are also sent to the phone automatically. " +
        "Never send passwords, keys or private documents.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        try
        {
            switch (Arg("action").ToLowerInvariant())
            {
                case "setup":
                    var s = _alerts.Enable(Arg("server"));
                    return new ToolResult(true,
                        "Phone alerts are on. On your phone:\n" +
                        "1. Install the free 'ntfy' app (Play Store).\n" +
                        $"2. Tap +, subscribe to topic: {s.Topic} (server {s.Server}).\n" +
                        "3. In the Da Fit / Timex watch app, allow notifications from ntfy so they reach the watch.\n" +
                        "Then say 'send a test alert'. Keep the topic name private: anyone who knows it can read the alerts.");
                case "send":
                    var message = Arg("message");
                    if (message.Length == 0) return new ToolResult(false, "message is required.");
                    var sent = await _alerts.SendAsync(Arg("title").Length > 0 ? Arg("title") : "JARVIS", message,
                        bool.TryParse(Arg("urgent"), out var u) && u, cancellationToken);
                    return sent ? new ToolResult(true, "Sent to your phone.") : new ToolResult(false, "Phone alerts are off. Run setup first.");
                case "off":
                    _alerts.Disable();
                    return new ToolResult(true, "Phone alerts are off.");
                case "status":
                    var st = _alerts.Settings();
                    return new ToolResult(true, st.Enabled ? $"Phone alerts are on (topic {st.Topic})." : "Phone alerts are off.");
                default:
                    return new ToolResult(false, "action must be setup, send, off or status.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new ToolResult(false, "Phone alert failed: " + ex.Message);
        }
    }
}
