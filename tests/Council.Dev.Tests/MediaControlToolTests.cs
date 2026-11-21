using Council.App.Media;
using Xunit;

namespace Council.Dev.Tests;

public sealed class MediaControlToolTests : IDisposable
{
    private readonly string _music = Path.Combine(Path.GetTempPath(), "jarvis-music-" + Guid.NewGuid().ToString("N"));
    private readonly List<byte> _keys = new();
    private readonly List<string> _opened = new();
    private readonly MediaControlTool _tool;

    public MediaControlToolTests()
    {
        Directory.CreateDirectory(Path.Combine(_music, "AR Rahman"));
        File.WriteAllText(Path.Combine(_music, "AR Rahman", "Jai Ho.mp3"), "");
        File.WriteAllText(Path.Combine(_music, "Believer - Imagine Dragons.m4a"), "");
        File.WriteAllText(Path.Combine(_music, "Jai Ho lyrics.txt"), "");
        _tool = new MediaControlTool(k => _keys.Add(k), t => { _opened.Add(t); return true; }, _music);
    }

    public void Dispose()
    {
        if (Directory.Exists(_music)) Directory.Delete(_music, recursive: true);
    }

    private Task<Council.Execution.Tools.ToolResult> Run(params (string Key, string Value)[] args) =>
        _tool.ExecuteAsync(args.ToDictionary(a => a.Key, a => a.Value));

    [Fact]
    public async Task SendsMediaKeys()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True((await Run(("action", "play_pause"))).Success);
        Assert.True((await Run(("action", "next"))).Success);
        Assert.True((await Run(("action", "volume_down"), ("steps", "3"))).Success);
        Assert.Equal(new byte[] { 0xB3, 0xB0, 0xAE, 0xAE, 0xAE }, _keys);
    }

    [Fact]
    public async Task PlaysTheBestMatchingSongFromTheMusicFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await Run(("action", "play_song"), ("query", "jai ho"));
        Assert.True(result.Success);
        Assert.EndsWith("Jai Ho.mp3", Assert.Single(_opened));
    }

    [Fact]
    public async Task SuggestsOnlineSearchWhenNoSongMatches()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await Run(("action", "play_song"), ("query", "shape of you"));
        Assert.False(result.Success);
        Assert.Contains("search_online", result.Message);
        Assert.Empty(_opened);
    }

    [Fact]
    public async Task OpensAYouTubeMusicSearch()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.True((await Run(("action", "search_online"), ("query", "naatu naatu"))).Success);
        Assert.Equal("https://music.youtube.com/search?q=naatu%20naatu", Assert.Single(_opened));
    }
}
