using Council.Dev.Terminal;
using Council.Dev.Validation;

namespace Council.Dev.Tests;

public class BuildTestExecutorTests
{
    [Fact]
    public async Task Successful_build_and_tests_return_passed()
    {
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult());

        var executor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        var result = await executor.RunAsync();

        Assert.True(result.Success);
        Assert.Equal(
            "Build and tests passed.",
            result.Message);

        Assert.NotNull(result.BuildResult);
        Assert.NotNull(result.TestResult);

        Assert.Equal(2, fake.Commands.Count);
        Assert.Equal("dotnet build", fake.Commands[0].Command);
        Assert.Equal("dotnet test", fake.Commands[1].Command);
    }

    [Fact]
    public async Task Failed_build_stops_before_tests()
    {
        var fake = new FakeTerminalExecutor(
            TerminalResult.FailedResult());

        var executor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        var result = await executor.RunAsync();

        Assert.False(result.Success);
        Assert.Equal("Build failed.", result.Message);
        Assert.Single(fake.Commands);
        Assert.Equal("dotnet build", fake.Commands[0].Command);
    }

    [Fact]
    public async Task Failed_tests_return_failure()
    {
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.FailedResult());

        var executor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        var result = await executor.RunAsync();

        Assert.False(result.Success);
        Assert.Equal("Tests failed.", result.Message);

        Assert.Equal(2, fake.Commands.Count);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult());

        var executor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.RunAsync(cancellationSource.Token));
    }

    [Fact]
    public void Missing_project_directory_is_rejected()
    {
        var fake = new FakeTerminalExecutor();

        Assert.Throws<DirectoryNotFoundException>(
            () => new BuildTestExecutor(
                fake,
                Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString("N"))));
    }

    private sealed class FakeTerminalExecutor : ITerminalExecutor
    {
        private readonly Queue<TerminalResult> _results;

        public List<TerminalCommand> Commands { get; } = [];

        public FakeTerminalExecutor(params TerminalResult[] results)
        {
            _results = new Queue<TerminalResult>(results);
        }

        public Task<TerminalResult> ExecuteAsync(
            TerminalCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Commands.Add(command);

            if (_results.Count == 0)
            {
                throw new InvalidOperationException(
                    "No fake terminal result was configured.");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}

