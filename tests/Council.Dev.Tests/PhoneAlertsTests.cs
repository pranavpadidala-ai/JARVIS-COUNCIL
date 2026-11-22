using System.Net;
using Council.App.Notify;
using Xunit;

namespace Council.Dev.Tests;

public sealed class PhoneAlertsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-alerts-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly RecordingHandler _handler = new();

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Url, string Title, string Body)> Sent { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add((request.RequestUri!.ToString(), request.Headers.GetValues("Title").Single(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task SendsNothingUntilSetUp()
    {
        var alerts = new PhoneAlerts(_path, new HttpClient(_handler));
        Assert.False(await alerts.SendAsync("JARVIS", "hello"));
        Assert.Empty(_handler.Sent);
    }

    [Fact]
    public async Task SetupCreatesASecretTopicAndSends()
    {
        var alerts = new PhoneAlerts(_path, new HttpClient(_handler));
        var tool = new PhoneAlertTool(alerts);
        var setup = await tool.ExecuteAsync(new() { ["action"] = "setup" });
        Assert.Contains("ntfy", setup.Message);
        var topic = alerts.Settings().Topic;
        Assert.StartsWith("jarvis-", topic);
        Assert.True(topic.Length >= 27);

        var sent = await tool.ExecuteAsync(new() { ["action"] = "send", ["message"] = "Gym at 6", ["title"] = "నమస్తే Reminder" });
        Assert.True(sent.Success);
        var (url, title, body) = Assert.Single(_handler.Sent);
        Assert.Equal($"https://ntfy.sh/{topic}", url);
        Assert.Equal("Reminder", title);
        Assert.Equal("Gym at 6", body);

        Assert.Equal(topic, alerts.Enable().Topic); // topic stays the same
        await tool.ExecuteAsync(new() { ["action"] = "off" });
        Assert.False(await alerts.SendAsync("JARVIS", "x"));
    }

    [Fact]
    public void RejectsPlainHttpServers()
    {
        var alerts = new PhoneAlerts(_path, new HttpClient(_handler));
        Assert.Throws<ArgumentException>(() => alerts.Enable("http://example.com"));
    }
}
