using System.Diagnostics;
using System.Text.Json;

namespace Council.Dev.Browser;

public sealed class BraveProfileManager : IDisposable
{
    private sealed record StoredProfiles(List<BraveProfile> Profiles);
    private sealed record RunningSession(Process Process, FileStream Lock, BraveProfileMode Mode);

    private readonly string _root;
    private readonly string _metadataPath;
    private readonly string? _configuredExecutable;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, RunningSession> _running = new(StringComparer.OrdinalIgnoreCase);
    private List<BraveProfile> _profiles = [];
    private bool _initialized;

    public BraveProfileManager(string? dataRoot = null, string? braveExecutable = null)
    {
        _root = Path.GetFullPath(dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JARVIS", "BraveProfiles"));
        _metadataPath = Path.Combine(_root, "profiles.json");
        _configuredExecutable = braveExecutable;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(_root);
            if (File.Exists(_metadataPath))
            {
                var info = new FileInfo(_metadataPath);
                if (info.Length > 1024 * 1024) throw new InvalidDataException("Brave profile metadata exceeds 1 MB.");
                await using var stream = File.OpenRead(_metadataPath);
                var stored = await JsonSerializer.DeserializeAsync<StoredProfiles>(stream, _json, cancellationToken);
                _profiles = stored?.Profiles ?? [];
                foreach (var profile in _profiles) ValidateProfile(profile);
            }
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<BraveProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try { return _profiles.ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<BraveProfile> CreateProfileAsync(string label, string purpose, bool temporary = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Profile label is required.", nameof(label));
        if (string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Profile purpose is required.", nameof(purpose));
        if (label.Length > 80 || purpose.Length > 300) throw new ArgumentException("Profile label or purpose is too long.");
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_profiles.Any(p => string.Equals(p.Label, label.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A Brave profile with this label already exists.");
            var id = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(_root, "data", id);
            Directory.CreateDirectory(directory);
            var profile = new BraveProfile(id, label.Trim(), purpose.Trim(), directory, temporary, DateTime.UtcNow);
            _profiles.Add(profile);
            try { await SaveMetadataAsync(cancellationToken); }
            catch { _profiles.Remove(profile); Directory.Delete(directory, true); throw; }
            return profile;
        }
        finally { _gate.Release(); }
    }

    public async Task<BraveProfileStatus> GetProfileStatusAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profile = Find(profileId);
            if (_running.TryGetValue(profileId, out var session))
            {
                if (!session.Process.HasExited)
                    return new BraveProfileStatus(profile, true, session.Mode, session.Process.Id);
                _running.Remove(profileId);
                session.Process.Dispose();
                session.Lock.Dispose();
            }
            return new BraveProfileStatus(profile, false, null, null);
        }
        finally { _gate.Release(); }
    }

    public async Task<BraveProfileStatus> LaunchProfileAsync(string profileId, BraveProfileMode mode,
        string? startUrl = null, CancellationToken cancellationToken = default)
    {
        if (startUrl is not null && (!Uri.TryCreate(startUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            throw new ArgumentException("Start URL must be an absolute HTTP or HTTPS URL.", nameof(startUrl));
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profile = Find(profileId);
            if (_running.TryGetValue(profileId, out var existing))
            {
                if (!existing.Process.HasExited)
                    return new BraveProfileStatus(profile, true, existing.Mode, existing.Process.Id);
                _running.Remove(profileId);
                existing.Process.Dispose();
                existing.Lock.Dispose();
            }
            var executable = ResolveExecutable();
            var lockPath = Path.Combine(profile.DataDirectory, ".jarvis-profile.lock");
            FileStream profileLock;
            try { profileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new InvalidOperationException("This Brave profile is already in use by another JARVIS session."); }

            Process? startedProcess = null;
            try
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = profile.DataDirectory };
                start.ArgumentList.Add("--user-data-dir=" + profile.DataDirectory);
                start.ArgumentList.Add("--no-first-run");
                start.ArgumentList.Add("--disable-sync");
                if (mode == BraveProfileMode.Headless) { start.ArgumentList.Add("--headless=new"); start.ArgumentList.Add("--disable-gpu"); }
                if (startUrl is not null) start.ArgumentList.Add(startUrl);
                startedProcess = Process.Start(start) ?? throw new InvalidOperationException("Brave did not start.");
                _running[profileId] = new RunningSession(startedProcess, profileLock, mode);
                var updated = profile with { LastUsedAtUtc = DateTime.UtcNow };
                _profiles[_profiles.FindIndex(p => p.Id == profileId)] = updated;
                await SaveMetadataAsync(cancellationToken);
                return new BraveProfileStatus(updated, true, mode, startedProcess.Id);
            }
            catch
            {
                _running.Remove(profileId);
                if (startedProcess is not null)
                {
                    try { if (!startedProcess.HasExited) startedProcess.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    startedProcess.Dispose();
                }
                profileLock.Dispose();
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task CloseProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Find(profileId);
            if (!_running.Remove(profileId, out var session)) return;
            try
            {
                if (!session.Process.HasExited)
                {
                    session.Process.CloseMainWindow();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    try { await session.Process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    { session.Process.Kill(entireProcessTree: true); await session.Process.WaitForExitAsync(cancellationToken); }
                }
            }
            finally { session.Process.Dispose(); session.Lock.Dispose(); }
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteTemporaryProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var profile = Find(profileId);
            if (!profile.IsTemporary) throw new InvalidOperationException("Persistent Brave profiles cannot be deleted by temporary cleanup.");
            if (_running.ContainsKey(profileId)) throw new InvalidOperationException("Close the Brave profile before deleting it.");
            _profiles.Remove(profile);
            try { await SaveMetadataAsync(cancellationToken); }
            catch { _profiles.Add(profile); throw; }
            var expected = Path.GetFullPath(Path.Combine(_root, "data", profile.Id));
            if (!expected.StartsWith(Path.GetFullPath(Path.Combine(_root, "data")) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Profile data path escaped the configured browser-data root.");
            if (Directory.Exists(expected)) Directory.Delete(expected, true);
        }
        finally { _gate.Release(); }
    }

    private BraveProfile Find(string id) => _profiles.FirstOrDefault(p => p.Id == id)
        ?? throw new KeyNotFoundException("Brave profile was not found.");

    private void ValidateProfile(BraveProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Contains(Path.DirectorySeparatorChar) ||
            profile.Id.Contains(Path.AltDirectorySeparatorChar))
            throw new InvalidDataException("Invalid Brave profile ID.");
        var expected = Path.GetFullPath(Path.Combine(_root, "data", profile.Id));
        if (!string.Equals(Path.GetFullPath(profile.DataDirectory), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Brave profile data directory is outside its configured root.");
    }

    private string ResolveExecutable()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_configuredExecutable)) candidates.Add(_configuredExecutable);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.AddRange([
            Path.Combine(pf, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(pf86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            Path.Combine(local, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
        ]);
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Brave executable not found. Configure its full path in JARVIS settings.");
    }

    private async Task SaveMetadataAsync(CancellationToken cancellationToken)
    {
        var temp = _metadataPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, new StoredProfiles(_profiles), _json, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temp, _metadataPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Dispose()
    {
        foreach (var session in _running.Values)
        {
            session.Process.Dispose();
            session.Lock.Dispose();
        }
        _running.Clear();
        _gate.Dispose();
    }
}