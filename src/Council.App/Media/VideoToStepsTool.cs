using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Council.App.Voice;
using Council.Execution.Tools;
using NAudio.Wave;
using Whisper.net;

namespace Council.App.Media;

/// <summary>
/// Turns a tutorial or screen-recording video into time-stamped notes: what is said (local Whisper) and
/// what is on screen at each visible change (OpenCV key frames + Windows OCR). The assistant then
/// rewrites the notes as numbered steps. Everything runs on this laptop; nothing is uploaded.
/// </summary>
public sealed class VideoToStepsTool : ITool
{
    public delegate Task<VideoEvidence> Extractor(string video, int maxMinutes, CancellationToken token);

    public sealed record Line(double At, string Kind, string Text);

    public sealed record VideoEvidence(double DurationSeconds, IReadOnlyList<Line> Lines);

    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".wmv" };
    private readonly Extractor _extract;
    private readonly string _notesFolder;

    public VideoToStepsTool(Extractor? extract = null, string? notesFolder = null)
    {
        _extract = extract ?? ExtractAsync;
        _notesFolder = notesFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JARVIS-VideoNotes");
    }

    public string Name => "video_to_steps";

    public string Description =>
        "Turn a tutorial or screen-recording video on this laptop into step-by-step instructions. Arguments: video (path), " +
        "optional max_minutes (1-30, default 15). Returns time-stamped speech and on-screen text and saves them in Documents\\JARVIS-VideoNotes. " +
        "Then write the workflow as numbered steps with the time of each step, noting any menu names, settings or values seen on screen. " +
        "Slow on this laptop (roughly the video's length); tell the user before starting a long video.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        var video = args.TryGetValue("video", out var v) ? Environment.ExpandEnvironmentVariables(v.Trim().Trim('"')) : "";
        if (video.Length == 0) return new ToolResult(false, "video (path to the video file) is required.");
        video = Path.GetFullPath(video);
        if (!File.Exists(video)) return new ToolResult(false, $"Video not found: {video}");
        if (!VideoExtensions.Contains(Path.GetExtension(video), StringComparer.OrdinalIgnoreCase))
            return new ToolResult(false, "Use an mp4, mkv, webm, avi, mov or wmv video.");
        var maxMinutes = args.TryGetValue("max_minutes", out var mm) && int.TryParse(mm, out var m) ? Math.Clamp(m, 1, 30) : 15;

        VideoEvidence evidence;
        try { evidence = await _extract(video, maxMinutes, cancellationToken); }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception
                                       or JsonException or PlatformNotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return new ToolResult(false, "Could not process the video: " + ex.Message);
        }
        if (evidence.Lines.Count == 0)
            return new ToolResult(false, "Found no speech or readable on-screen text in that video.");

        var notes = Format(Path.GetFileName(video), evidence);
        Directory.CreateDirectory(_notesFolder);
        var notesPath = Path.Combine(_notesFolder, Path.GetFileNameWithoutExtension(video) + " - notes.md");
        await File.WriteAllTextAsync(notesPath, notes, cancellationToken);
        var shown = notes.Length > 12000 ? notes[..12000] + "\n... (rest in the saved notes file)" : notes;
        return new ToolResult(true, $"Notes saved to {notesPath}\n\n{shown}");
    }

    public static string Format(string name, VideoEvidence evidence)
    {
        var text = new StringBuilder($"# {name}\nLength {TimeSpan.FromSeconds(evidence.DurationSeconds):h\\:mm\\:ss}. [speech] = narration, [screen] = text visible on screen.\n\n");
        string? lastScreen = null;
        foreach (var line in evidence.Lines.OrderBy(l => l.At))
        {
            var t = line.Text.Trim();
            if (t.Length == 0 || line.Kind == "screen" && t == lastScreen) continue;
            if (line.Kind == "screen") { lastScreen = t; if (t.Length > 600) t = t[..600] + "..."; }
            text.Append($"[{TimeSpan.FromSeconds(line.At):m\\:ss}] [{line.Kind}] {t}\n");
        }
        return text.ToString();
    }

    private static async Task<VideoEvidence> ExtractAsync(string video, int maxMinutes, CancellationToken token)
    {
        var lines = new List<Line>();
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jarvis-video-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var duration = await TranscribeAsync(video, maxMinutes, lines, token);
            var frames = await KeyFramesAsync(video, temp, token);
            foreach (var (at, file) in frames.Where(f => f.At <= maxMinutes * 60))
            {
                var ocr = await OcrAsync(file, token);
                if (ocr.Length > 0) lines.Add(new Line(at, "screen", ocr));
            }
            return new VideoEvidence(duration, lines);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Reads the audio with Windows Media Foundation (no ffmpeg), resamples to 16 kHz mono and runs Whisper.</summary>
    private static async Task<double> TranscribeAsync(string video, int maxMinutes, List<Line> lines, CancellationToken token)
    {
        var model = Environment.GetEnvironmentVariable("JARVIS_WHISPER_MODEL_PATH") ?? Path.Combine(LocalModelPaths.ResolveRoot(), "models", "ggml-small.bin");
        using var reader = new MediaFoundationReader(video);
        var duration = reader.TotalTime.TotalSeconds;
        if (!File.Exists(model)) return duration; // speech skipped; screen text still works
        using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1)) { ResamplerQuality = 60 };
        await using var wav = new MemoryStream();
        var limit = (long)maxMinutes * 60 * 16000 * 2;
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(wav), resampler.WaveFormat))
        {
            var buffer = new byte[16000 * 2];
            int read;
            long total = 0;
            while (total < limit && (read = resampler.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                writer.Write(buffer, 0, read);
                total += read;
            }
        }
        wav.Position = 0;
        using var factory = WhisperFactory.FromPath(model);
        using var processor = factory.CreateBuilder().WithLanguage("auto").WithThreads(Math.Max(1, Environment.ProcessorCount / 2)).Build();
        await foreach (var segment in processor.ProcessAsync(wav).WithCancellation(token))
            if (segment.Text.Trim().Length > 0)
                lines.Add(new Line(segment.Start.TotalSeconds, "speech", segment.Text.Trim()));
        return duration;
    }

    private static async Task<List<(double At, string File)>> KeyFramesAsync(string video, string folder, CancellationToken token)
    {
        var python = Environment.GetEnvironmentVariable("JARVIS_WORKOUT_PYTHON") ?? Path.Combine(LocalModelPaths.ResolveRoot(), "workout-venv", "Scripts", "python.exe");
        var script = FindRepoFile(Path.Combine("python", "jarvis_runtime", "video_keyframes.py"));
        if (!File.Exists(python) || script is null) return new();
        var output = await RunAsync(python, new[] { script, video, folder, "--every", "2", "--max", "30" }, token);
        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'));
        if (line is null) return new();
        using var doc = JsonDocument.Parse(line);
        if (!doc.RootElement.GetProperty("ok").GetBoolean()) return new();
        return doc.RootElement.GetProperty("frames").EnumerateArray()
            .Select(f => (f.GetProperty("at").GetDouble(), f.GetProperty("file").GetString()!)).ToList();
    }

    private static async Task<string> OcrAsync(string image, CancellationToken token)
    {
        var script = FindRepoFile(Path.Combine("scripts", "Convert-ScannedPdfToText.ps1"));
        if (script is null) return "";
        var output = Path.ChangeExtension(image, ".txt");
        await RunAsync("powershell.exe", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Pdf", image, "-Output", output }, token);
        if (!File.Exists(output)) return "";
        var text = string.Join(" ", (await File.ReadAllLinesAsync(output, token))
            .Where(l => !l.StartsWith("=== PAGE", StringComparison.Ordinal)).Select(l => l.Trim()).Where(l => l.Length > 0));
        return text;
    }

    private static async Task<string> RunAsync(string exe, IEnumerable<string> args, CancellationToken token)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + exe);
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        _ = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return await stdout;
    }

    private static string? FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Lets WaveFileWriter finish its header without closing the memory stream.</summary>
    private sealed class IgnoreDisposeStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) => Flush();
    }
}
