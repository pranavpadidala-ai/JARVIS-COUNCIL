using Council.AI.Providers;
using Council.Core.Models;
using Council.Core.Providers;

namespace Council.Dev.Tests;

public sealed class FallbackAIProviderTests
{
    [Fact]
    public async Task Uses_next_provider_when_primary_fails()
    {
        var primary = new StubProvider("Primary", shouldFail: true);
        var backup = new StubProvider("Backup", shouldFail: false);
        var router = new FallbackAIProvider([primary, backup]);

        var response = await router.AskAsync(CreateRequest());

        Assert.Equal("Backup", response.Provider);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, backup.CallCount);
    }

    [Fact]
    public async Task Does_not_call_backup_after_primary_succeeds()
    {
        var primary = new StubProvider("Primary", shouldFail: false);
        var backup = new StubProvider("Backup", shouldFail: false);
        var router = new FallbackAIProvider([primary, backup]);

        var response = await router.AskAsync(CreateRequest());

        Assert.Equal("Primary", response.Provider);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(0, backup.CallCount);
    }

    [Fact]
    public async Task Reports_all_provider_failures()
    {
        var router = new FallbackAIProvider(
        [
            new StubProvider("Gemini", shouldFail: true),
            new StubProvider("Gateway", shouldFail: true)
        ]);

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => router.AskAsync(CreateRequest()));

        Assert.Equal(2, exception.InnerExceptions.Count);
    }

    [Fact]
    public async Task Propagates_cancellation_without_failing_over()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var primary = new StubProvider("Primary", shouldFail: false);
        var backup = new StubProvider("Backup", shouldFail: false);
        var router = new FallbackAIProvider([primary, backup]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => router.AskAsync(CreateRequest(), cancellation.Token));

        Assert.Equal(0, primary.CallCount);
        Assert.Equal(0, backup.CallCount);
    }

    private static AIRequest CreateRequest() =>
        new([new AIMessage("user", "Hello")]);

    private sealed class StubProvider(string name, bool shouldFail) : IAIProvider
    {
        public string Name => name;
        public int CallCount { get; private set; }

        public Task<AIResponse> AskAsync(
            AIRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (shouldFail)
                throw new InvalidOperationException($"{name} is unavailable.");

            return Task.FromResult(new AIResponse(name, "success", "test-model"));
        }
    }
}
