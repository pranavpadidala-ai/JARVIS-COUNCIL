using System.Text.Json;
using Council.Dev.Browser;
using Council.Execution.Tools;

namespace Council.App;

public sealed class BraveProfileTool : ITool
{
    private readonly BraveProfileManager _manager;

    public BraveProfileTool(BraveProfileManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
    }

    public string Name => "brave_profiles";
    public string Description =>
        "Manage isolated Brave browser profiles for legitimate browsing, research, testing and user-authorized GUI tasks. " +
        "Actions: list; create (label, purpose, optional temporary=true|false); launch (profile_id, mode=headless|visible, optional http(s) url); " +
        "status (profile_id); close (profile_id); delete_temp (profile_id). " +
        "Never use profiles to evade provider quotas or rotate consumer AI logins. Authentication state is never copied or exported.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("action", out var action))
            return new ToolResult(false, "Required argument: action.");
        try
        {
            await _manager.InitializeAsync(cancellationToken);
            switch (action.Trim().ToLowerInvariant())
            {
                case "list":
                {
                    if (!Only("action")) return Invalid();
                    var profiles = await _manager.ListProfilesAsync(cancellationToken);
                    return new ToolResult(true, profiles.Count == 0
                        ? "No Brave profiles are configured."
                        : JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
                }
                case "create":
                {
                    if (!Only("action", "label", "purpose", "temporary") ||
                        !arguments.TryGetValue("label", out var label) ||
                        !arguments.TryGetValue("purpose", out var purpose))
                        return new ToolResult(false, "create requires label and purpose; temporary is optional.");
                    var temporary = arguments.TryGetValue("temporary", out var t) &&
                        bool.TryParse(t, out var parsed) && parsed;
                    var profile = await _manager.CreateProfileAsync(label, purpose, temporary, cancellationToken);
                    return new ToolResult(true, "Created isolated Brave profile: " +
                        JsonSerializer.Serialize(profile));
                }
                case "launch":
                {
                    if (!Only("action", "profile_id", "mode", "url") ||
                        !arguments.TryGetValue("profile_id", out var id))
                        return new ToolResult(false, "launch requires profile_id; mode=headless|visible and url are optional.");
                    var modeText = arguments.TryGetValue("mode", out var m) ? m : "visible";
                    if (!Enum.TryParse<BraveProfileMode>(modeText, true, out var mode))
                        return new ToolResult(false, "mode must be headless or visible.");
                    arguments.TryGetValue("url", out var url);
                    var status = await _manager.LaunchProfileAsync(id, mode, url, cancellationToken);
                    return new ToolResult(true, "Brave profile launched: " + JsonSerializer.Serialize(status));
                }
                case "status":
                {
                    if (!Only("action", "profile_id") || !arguments.TryGetValue("profile_id", out var id))
                        return new ToolResult(false, "status requires profile_id.");
                    var status = await _manager.GetProfileStatusAsync(id, cancellationToken);
                    return new ToolResult(true, JsonSerializer.Serialize(status));
                }
                case "close":
                {
                    if (!Only("action", "profile_id") || !arguments.TryGetValue("profile_id", out var id))
                        return new ToolResult(false, "close requires profile_id.");
                    await _manager.CloseProfileAsync(id, cancellationToken);
                    return new ToolResult(true, "Brave profile close requested.");
                }
                case "delete_temp":
                {
                    if (!Only("action", "profile_id") || !arguments.TryGetValue("profile_id", out var id))
                        return new ToolResult(false, "delete_temp requires profile_id.");
                    await _manager.DeleteTemporaryProfileAsync(id, cancellationToken);
                    return new ToolResult(true, "Temporary Brave profile and its isolated data directory were deleted.");
                }
                default:
                    return new ToolResult(false, "Unknown action. Use list, create, launch, status, close, or delete_temp.");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or
                                   ArgumentException or KeyNotFoundException or InvalidDataException or JsonException)
        {
            return new ToolResult(false, "Brave profile operation failed safely: " + ex.Message);
        }

        bool Only(params string[] allowed) => arguments.Keys.All(k => allowed.Contains(k, StringComparer.OrdinalIgnoreCase));
        static ToolResult Invalid() => new(false, "Unexpected arguments were supplied.");
    }
}