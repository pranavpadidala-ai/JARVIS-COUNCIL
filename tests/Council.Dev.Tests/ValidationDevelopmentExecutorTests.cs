using Council.Dev.Execution;
using Council.Dev.Tasks;
using Council.Dev.Terminal;
using Council.Dev.Validation;

namespace Council.Dev.Tests;

public class ValidationDevelopmentExecutorTests
{
    [Fact]
    public async Task Successful_validation_returns_completed()
    {
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult());

        var buildTestExecutor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        var executor = new ValidationDevelopmentExecutor(
            buildTestExecutor);

        var task = CreateTask();

        var result = await executor.ExecuteAsync(task);

        Assert.True(result.Success);
        Assert.Equal(
            "Build and tests passed.",
            result.Message);

        Assert.Equal(2, fake.Commands.Count);
    }

    [Fact]
    public async Task Failed_build_returns_failed()
    {
        var fake = new FakeTerminalExecutor(
            TerminalResult.FailedResult());

        var buildTestExecutor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        var executor = new ValidationDevelopmentExecutor(
            buildTestExecutor);

        var result = await executor.ExecuteAsync(CreateTask());

        Assert.False(result.Success);
        Assert.Equal(
            "Build failed.",
            result.Message);

        Assert.Single(fake.Commands);
    }

    [Fact]
    public async Task Failed_tests_returns_failed()
    {
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.FailedResult());

        var buildTestExecutor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        var executor = new ValidationDevelopmentExecutor(
            buildTestExecutor);

        var result = await executor.ExecuteAsync(CreateTask());

        Assert.False(result.Success);
        Assert.Equal(
            "Tests failed.",
            result.Message);

        Assert.Equal(2, fake.Commands.Count);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult());

        var buildTestExecutor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        var executor = new ValidationDevelopmentExecutor(
            buildTestExecutor);

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(
                CreateTask(),
                cancellationSource.Token));
    }

    [Fact]
    public async Task Null_task_is_rejected()
    {
        var fake = new FakeTerminalExecutor();

        var buildTestExecutor = new BuildTestExecutor(
            fake,
            Directory.GetCurrentDirectory());

        var executor = new ValidationDevelopmentExecutor(
            buildTestExecutor);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => executor.ExecuteAsync(null!));
    }

    private static DevelopmentTask CreateTask()
    {
        return new DevelopmentTask
        {
            Title = "Test development task",
            Description = "Test development task description"
        };
    }

    private sealed class FakeTerminalExecutor : ITerminalExecutor
    {
        private readonly Queue<TerminalResult> _results;

        public List<TerminalCommand> Commands { get; } = [];

        public FakeTerminalExecutor(
            params TerminalResult[] results)
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

            return Task.FromResult(
                _results.Dequeue());
        }
    }
}

