using Council.Dev.Terminal;
using Council.Dev.Validation;

namespace Council.Dev.Tests;

public sealed class BuildTestExecutorTests
{
    [Fact]
    public async Task Successful_build_and_dotnet_tests_pass_without_python_suite()
    {
        using var workspace = new TemporaryWorkspace();
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult());

        var result = await new BuildTestExecutor(fake, workspace.Path).RunAsync();

        Assert.True(result.Success);
        Assert.Equal("Build and .NET tests passed.", result.Message);
        Assert.NotNull(result.BuildResult);
        Assert.NotNull(result.TestResult);
        Assert.Null(result.PythonValidationResult);
        Assert.Equal(2, fake.Commands.Count);
    }

    [Fact]
    public async Task Successful_validation_compiles_python_then_runs_unit_tests()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "tests", "python"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "python"));
        Directory.CreateDirectory(Path.Combine(workspace.Path, "scripts"));
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult());

        var result = await new BuildTestExecutor(fake, workspace.Path).RunAsync();

        Assert.True(result.Success);
        Assert.Equal(
            "Build, .NET tests, Python syntax checks, and Python unit tests passed.",
            result.Message);
        Assert.Equal(4, fake.Commands.Count);
        Assert.Equal("dotnet build", fake.Commands[0].Command);
        Assert.Equal("dotnet test", fake.Commands[1].Command);
        Assert.Equal("py -3.11 -m compileall -q python tests/python scripts", fake.Commands[2].Command);
        Assert.Equal("py -3.11 -m unittest discover -s tests/python -v", fake.Commands[3].Command);
        Assert.Equal(TimeSpan.FromMinutes(2), fake.Commands[2].Timeout);
        Assert.Equal(TimeSpan.FromMinutes(3), fake.Commands[3].Timeout);
    }

    [Fact]
    public async Task Failed_python_syntax_check_stops_before_unit_tests()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "tests", "python"));
        var syntaxFailure = new TerminalResult(1, "bad file.py", "SyntaxError", TimeSpan.FromSeconds(1), false, false);
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult(),
            syntaxFailure);

        var result = await new BuildTestExecutor(fake, workspace.Path).RunAsync();

        Assert.False(result.Success);
        Assert.Equal("Python syntax validation failed.", result.Message);
        Assert.Equal(syntaxFailure, result.PythonValidationResult);
        Assert.Equal(3, fake.Commands.Count);
    }

    [Fact]
    public async Task Failed_python_unit_tests_return_python_validation_failure()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "tests", "python"));
        var pythonFailure = new TerminalResult(1, "test output", "test failure", TimeSpan.FromSeconds(1), false, false);
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult(),
            TerminalResult.SuccessResult(),
            pythonFailure);

        var result = await new BuildTestExecutor(fake, workspace.Path).RunAsync();

        Assert.False(result.Success);
        Assert.Equal("Python unit tests failed.", result.Message);
        Assert.Equal(pythonFailure, result.PythonValidationResult);
        Assert.Equal(4, fake.Commands.Count);
    }

    [Fact]
    public async Task Failed_build_stops_before_tests()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "tests", "python"));
        var fake = new FakeTerminalExecutor(TerminalResult.FailedResult());

        var result = await new BuildTestExecutor(fake, workspace.Path).RunAsync();

        Assert.False(result.Success);
        Assert.Equal("Build failed.", result.Message);
        Assert.Single(fake.Commands);
    }

    [Fact]
    public async Task Failed_dotnet_tests_stop_before_python_validation()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "tests", "python"));
        var fake = new FakeTerminalExecutor(
            TerminalResult.SuccessResult(),
            TerminalResult.FailedResult());

        var result = await new BuildTestExecutor(fake, workspace.Path).RunAsync();

        Assert.False(result.Success);
        Assert.Equal("Tests failed.", result.Message);
        Assert.Equal(2, fake.Commands.Count);
        Assert.Null(result.PythonValidationResult);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var workspace = new TemporaryWorkspace();
        var fake = new FakeTerminalExecutor(TerminalResult.SuccessResult());
        var executor = new BuildTestExecutor(fake, workspace.Path);

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
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
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
                throw new InvalidOperationException("No fake terminal result was configured.");
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "JARVIS-BuildTest", Guid.NewGuid().ToString("N"));

        public TemporaryWorkspace() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
