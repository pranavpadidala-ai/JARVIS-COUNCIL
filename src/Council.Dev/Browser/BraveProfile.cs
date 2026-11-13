namespace Council.Dev.Browser;

public enum BraveProfileMode { Headless, Visible }

public sealed record BraveProfile(
    string Id,
    string Label,
    string Purpose,
    string DataDirectory,
    bool IsTemporary,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc = null);

public sealed record BraveProfileStatus(
    BraveProfile Profile,
    bool IsRunning,
    BraveProfileMode? Mode,
    int? ProcessId);