using Council.App.Phone;

namespace Council.Dev.Tests;

public sealed class PhoneCommandInboxTests
{
    [Fact]
    public async Task Enqueue_IsIdempotentAndSurvivesReload()
    {
        var path = CreatePath();
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

            var claimed = await reloaded.ClaimAsync(first.Id);
            Assert.NotNull(claimed);
            Assert.Equal(PhoneCommandStatus.Processing, claimed!.Status);
            Assert.Null(await inbox.GetNextAsync());
            Assert.Null(await inbox.ClaimAsync(first.Id));

            Assert.True(await inbox.CompleteAsync(first.Id, "Three key findings."));
            Assert.False(await reloaded.CancelAsync(first.Id));
            var history = await new PhoneCommandInbox(path).ListAsync();
            var completed = Assert.Single(history);
            Assert.Equal(PhoneCommandStatus.Completed, completed.Status);
            Assert.Equal("Three key findings.", completed.Result);
        }
        finally
        {
            DeleteStore(path);
        }
    }

    [Fact]
    public async Task CancelledCommandIsNotReturnedAsNext()
    {
        var path = CreatePath();
        try
        {
            var inbox = new PhoneCommandInbox(path);
            var command = await inbox.EnqueueAsync("request-0002", "Open a project file");
            Assert.True(await inbox.CancelAsync(command.Id));
            Assert.Null(await inbox.GetNextAsync());
            Assert.Null(await inbox.ClaimAsync(command.Id));
        }
        finally
        {
            DeleteStore(path);
        }
    }

    [Fact]
    public async Task ConcurrentInboxInstances_CanClaimACommandOnlyOnce()
    {
        var path = CreatePath();
        try
        {
            var first = new PhoneCommandInbox(path);
            var second = new PhoneCommandInbox(path);
            var command = await first.EnqueueAsync("request-0004", "Read my latest document");

            var claims = await Task.WhenAll(
                first.ClaimAsync(command.Id),
                second.ClaimAsync(command.Id));

            Assert.Single(claims.Where(claim => claim is not null));
            var saved = Assert.Single(await first.ListAsync());
            Assert.Equal(PhoneCommandStatus.Processing, saved.Status);
        }
        finally
        {
            DeleteStore(path);
        }
    }

    [Fact]
    public async Task FailedCommandStoresSafeResultAndIsNotClaimableAgain()
    {
        var path = CreatePath();
        try
        {
            var inbox = new PhoneCommandInbox(path);
            var command = await inbox.EnqueueAsync("request-0005", "Analyze this project");
            Assert.NotNull(await inbox.ClaimAsync(command.Id));
            Assert.True(await inbox.FailAsync(command.Id, "JARVIS could not complete the request."));
            Assert.Null(await inbox.ClaimAsync(command.Id));

            var saved = Assert.Single(await new PhoneCommandInbox(path).ListAsync());
            Assert.Equal(PhoneCommandStatus.Failed, saved.Status);
            Assert.Equal("JARVIS could not complete the request.", saved.Result);
        }
        finally
        {
            DeleteStore(path);
        }
    }

    [Fact]
    public async Task RecentHistoryReturnsOnlyTheNewestRequestedCommands()
    {
        var path = CreatePath();
        try
        {
            var inbox = new PhoneCommandInbox(path);
            await inbox.EnqueueAsync("request-1001", "First request");
            await inbox.EnqueueAsync("request-1002", "Second request");
            await inbox.EnqueueAsync("request-1003", "Third request");

            var recent = await inbox.ListRecentAsync(2);

            Assert.Equal(2, recent.Count);
            Assert.Equal("Second request", recent[0].Text);
            Assert.Equal("Third request", recent[1].Text);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => inbox.ListRecentAsync(0));
        }
        finally
        {
            DeleteStore(path);
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
        var path = CreatePath();
        try
        {
            var inbox = new PhoneCommandInbox(path);
            var command = await inbox.EnqueueAsync("request-0003", "Explain this file");
            Assert.NotNull(await inbox.ClaimAsync(command.Id));
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
            DeleteStore(path);
        }
    }

    private static string CreatePath() =>
        Path.Combine(Path.GetTempPath(), "jarvis-phone-" + Guid.NewGuid().ToString("N") + ".json");

    private static void DeleteStore(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        if (File.Exists(path + ".lock"))
            File.Delete(path + ".lock");
    }
}
