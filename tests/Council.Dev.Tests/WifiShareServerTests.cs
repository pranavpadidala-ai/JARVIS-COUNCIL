using System.Net;
using System.Net.Http.Headers;
using Council.App.Transfer;
using Xunit;

namespace Council.Dev.Tests;

public sealed class WifiShareServerTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jarvis-share-" + Guid.NewGuid().ToString("N"));
    private readonly WifiShareServer _server;
    private readonly HttpClient _http = new();
    private string _link = "";

    public WifiShareServerTests() => _server = new WifiShareServer(_folder);

    public async Task InitializeAsync() => _link = (await _server.StartAsync(TimeSpan.FromMinutes(5), loopbackOnly: true))[0];

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        _http.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task NeedsTheSecretLink()
    {
        var withoutToken = new Uri(new Uri(_link), "/");
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(withoutToken)).StatusCode);
        Assert.Contains("JARVIS file share", await _http.GetStringAsync(_link));
    }

    [Fact]
    public async Task SharesALaptopFileForDownload()
    {
        var source = Path.Combine(Path.GetTempPath(), "jarvis-src-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(source, "HAZ notes");
        try
        {
            var name = _server.Share(source);
            Assert.Contains(name, await _http.GetStringAsync(_link));
            Assert.Equal("HAZ notes", await _http.GetStringAsync(_link + "file/" + Uri.EscapeDataString(name)));
            Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync(_link + "file/..%2F..%2Fsecret.txt")).StatusCode);
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public async Task SavesUploadsWithoutOverwriting()
    {
        var received = new List<string>();
        _server.Uploaded += names => received.AddRange(names);
        for (var i = 0; i < 2; i++)
        {
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent("photo"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(file, "files", "site photo.jpg");
            var response = await _http.PostAsync(_link + "upload", form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.True(File.Exists(Path.Combine(_folder, "site photo.jpg")));
        Assert.True(File.Exists(Path.Combine(_folder, "site photo (2).jpg")));
        Assert.Equal(2, received.Count);
    }

    [Fact]
    public async Task StopsCleanly()
    {
        await _server.StopAsync();
        Assert.False(_server.Running);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => _http.GetStringAsync(_link));
    }
}
