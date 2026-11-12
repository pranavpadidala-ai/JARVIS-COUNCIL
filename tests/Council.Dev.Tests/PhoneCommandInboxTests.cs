using Council.App.Phone;

namespace Council.Dev.Tests;

public sealed class PhoneCommandInboxTests
{
    [Fact]
    public async Task Enqueue_IsIdempotentAndSurvivesReload()
    {
        var path = Path.Combine(Path.GetTempPath(), "jarvis-phone-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var inbox = new PhoneCommandInbox(path);
            var first = await inbox.EnqueueAsync("request-0001", "Summarize my latest report");
            var duplicate = await inbox.EnqueueAsync("request-0001", "Different duplicate payload");
            Assert.Equal(first.Id, duplicate.Id);
            Assert.Equal("Summarize my latest report", duplicate.Text);

            var reloaded = new PhoneCommandInbox(path);
            var next = await reloaded.GetNextAsync();
            Assert.NotNull(next);
            Assert.Equal(first.Id, next!.Id);
            Assert.Equal("Summarize my latest report", next.Text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task CancelledCommandIsNotReturnedAsNext()
    {
        var path = Path.Combine(Path.GetTempPath(), "jarvis-phone-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var inbox = new PhoneCommandInbox(path);
            var command = await inbox.EnqueueAsync("request-0002", "Open a project file");
            Assert.True(await inbox.CancelAsync(command.Id));
            Assert.Null(await inbox.GetNextAsync());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void BridgeRejectsNonTailscaleAddresses()
    {
        Assert.Throws<ArgumentException>(() =>
            new PhoneCommandBridgeService("192.168.1.20", 47821, new string('x', 40)));
    }

    [Fact]
    public async Task CompletedCommandCannotBeCancelledAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), "jarvis-phone-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var inbox = new PhoneCommandInbox(path);
            var command = await inbox.EnqueueAsync("request-0003", "Explain this file");
            Assert.True(await inbox.CompleteAsync(command.Id, "The report contains three key findings."));
            Assert.False(await inbox.CancelAsync(command.Id));
            Assert.Null(await inbox.GetNextAsync());

            var history = await new PhoneCommandInbox(path).ListAsync();
            var completed = Assert.Single(history);
            Assert.Equal(PhoneCommandStatus.Completed, completed.Status);
            Assert.Equal("The report contains three key findings.", completed.Result);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
