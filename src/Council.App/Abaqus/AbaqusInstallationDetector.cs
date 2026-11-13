using System.Diagnostics;

namespace Council.App.Abaqus;

public sealed record AbaqusInstallationInfo(
    bool IsAvailable,
    string? CommandPath,
    string Message);

/// <summary>
/// Locates an Abaqus command launcher without starting Abaqus or changing any files.
/// The detected launcher version is not assumed; a later probe will verify it.
/// </summary>
public sealed class AbaqusInstallationDetector
{
    private static readonly string[] CandidateNames =
    [
        "abq2024.bat",
        "abq2024.cmd",
        "abaqus.bat",
        "abaqus.cmd",
        "abaqus.exe"
    ];

    private readonly string? _configuredCommand;
    private readonly IReadOnlyList<string> _searchDirectories;
    private readonly Func<string, string?> _pathLookup;

    public AbaqusInstallationDetector(
        string? configuredCommand = null,
        IEnumerable<string>? searchDirectories = null,
        Func<string, string?>? pathLookup = null)
    {
        _configuredCommand = configuredCommand ??
            Environment.GetEnvironmentVariable("ABAQUS_COMMAND");

        _searchDirectories = (searchDirectories ?? GetDefaultSearchDirectories())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _pathLookup = pathLookup ?? FindOnPath;
    }

    public AbaqusInstallationInfo Detect()
    {
        var configured = ResolveCommand(_configuredCommand);
        if (configured is not null)
        {
            return new AbaqusInstallationInfo(
                true,
                configured,
                "Abaqus command launcher found through ABAQUS_COMMAND. Its version will be verified during the first safe probe.");
        }

        foreach (var directory in _searchDirectories)
        {
            foreach (var candidate in CandidateNames)
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path))
                {
                    return new AbaqusInstallationInfo(
                        true,
                        Path.GetFullPath(path),
                        "Abaqus command launcher found in a standard or configured installation directory. Its version will be verified during the first safe probe.");
                }
            }
        }

        foreach (var command in new[] { "abq2024", "abaqus" })
        {
            var path = ResolveCommand(command);
            if (path is not null)
            {
                return new AbaqusInstallationInfo(
                    true,
                    path,
                    "Abaqus command launcher found on PATH. Its version will be verified during the first safe probe.");
            }
        }

        return new AbaqusInstallationInfo(
            false,
            null,
            "Abaqus command launcher was not detected. Abaqus/CAE may still be installed. Configure the full path to its command launcher in the user environment variable ABAQUS_COMMAND; do not share license files or credentials.");
    }

    private string? ResolveCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var candidate = command.Trim().Trim('"');

        try
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            var fromPath = _pathLookup(candidate);
            return !string.IsNullOrWhiteSpace(fromPath) && File.Exists(fromPath)
                ? Path.GetFullPath(fromPath)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> GetDefaultSearchDirectories()
    {
        var directories = new List<string>
        {
            @"C:\SIMULIA\Commands"
        };

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            directories.Add(Path.Combine(
                programFiles, "Dassault Systemes", "SIMULIA", "Commands"));
        }

        var abaqusHome = Environment.GetEnvironmentVariable("ABAQUS_HOME");
        if (!string.IsNullOrWhiteSpace(abaqusHome))
        {
            directories.Add(abaqusHome);
            directories.Add(Path.Combine(abaqusHome, "Commands"));
        }

        return directories;
    }

    private static string? FindOnPath(string command)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
                return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            if (process.ExitCode != 0)
                return null;

            return output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(File.Exists);
        }
        catch
        {
            return null;
        }
    }
}
