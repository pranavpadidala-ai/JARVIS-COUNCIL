using Council.Dev.Actions;
using Council.Dev.Terminal;
using Council.Dev.Workspace;

namespace Council.Dev.Tests;

public class DevelopmentActionExecutorTests
{
    [Fact]
    public async Task Read_file_action_is_forwarded_to_workspace_executor()
    {
        var root = CreateWorkspace();

        var workspace =
            new SafeWorkspaceExecutor(root);

        var terminal =
            new FakeTerminalExecutor();

        var executor =
            new DevelopmentActionExecutor(
                workspace,
                terminal,
                root);

        await File.WriteAllTextAsync(
            Path.Combine(root, "Test.cs"),
            "hello");

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.ReadFile,
                    "Test.cs"));

        Assert.True(result.Success);
        Assert.Equal("hello", result.Content);
        Assert.Empty(terminal.Commands);
    }

    [Fact]
    public async Task Create_file_action_is_forwarded_to_workspace_executor()
    {
        var root = CreateWorkspace();

        var workspace =
            new SafeWorkspaceExecutor(root);

        var terminal =
            new FakeTerminalExecutor();

        var executor =
            new DevelopmentActionExecutor(
                workspace,
                terminal,
                root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.CreateFile,
                    "src/Test.cs",
                    "public class Test {}"));

        Assert.True(result.Success);

        Assert.True(
            File.Exists(
                Path.Combine(root, "src", "Test.cs")));

        Assert.Empty(terminal.Commands);
    }

    [Fact]
    public async Task Build_action_executes_dotnet_build()
    {
        var root = CreateWorkspace();

        var workspace =
            new SafeWorkspaceExecutor(root);

        var terminal =
            new FakeTerminalExecutor(
                success: true,
                output: "Build succeeded.");

        var executor =
            new DevelopmentActionExecutor(
                workspace,
                terminal,
                root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.RunBuild,
                    "."));

        Assert.True(result.Success);

        Assert.Single(terminal.Commands);

        Assert.Equal(
            "dotnet build",
            terminal.Commands[0].Command);
    }

    [Fact]
    public async Task Test_action_executes_dotnet_test()
    {
        var root = CreateWorkspace();

        var workspace =
            new SafeWorkspaceExecutor(root);

        var terminal =
            new FakeTerminalExecutor(
                success: true,
                output: "Tests passed.");

        var executor =
            new DevelopmentActionExecutor(
                workspace,
                terminal,
                root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.RunTests,
                    "."));

        Assert.True(result.Success);

        Assert.Single(terminal.Commands);

        Assert.Equal(
            "dotnet test",
            terminal.Commands[0].Command);
    }

    [Fact]
    public async Task Git_diff_action_executes_git_diff()
    {
        var root = CreateWorkspace();

        var workspace =
            new SafeWorkspaceExecutor(root);

        var terminal =
            new FakeTerminalExecutor(
                success: true,
                output: "diff output");

        var executor =
            new DevelopmentActionExecutor(
                workspace,
                terminal,
                root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.GitDiff,
                    "."));

        Assert.True(result.Success);

        Assert.Single(terminal.Commands);

        Assert.Equal(
            "git diff",
            terminal.Commands[0].Command);
    }

    [Fact]
    public async Task Failed_terminal_command_returns_failed_result()
    {
        var root = CreateWorkspace();

        var workspace =
            new SafeWorkspaceExecutor(root);

        var terminal =
            new FakeTerminalExecutor(
                success: false,
                error: "Build failed.");

        var executor =
            new DevelopmentActionExecutor(
                workspace,
                terminal,
                root);

        var result =
            await executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.RunBuild,
                    "."));

        Assert.False(result.Success);
        Assert.Contains(
            "failed",
            result.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            "Build failed.",
            result.Content);
    }

    [Fact]
    public async Task Null_action_is_rejected()
    {
        var root = CreateWorkspace();

        var workspace =
            new SafeWorkspaceExecutor(root);

        var terminal =
            new FakeTerminalExecutor();

        var executor =
            new DevelopmentActionExecutor(
                workspace,
                terminal,
                root);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => executor.ExecuteAsync(null!));
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var root = CreateWorkspace();

        var workspace =
            new SafeWorkspaceExecutor(root);

        var terminal =
            new FakeTerminalExecutor();

        var executor =
            new DevelopmentActionExecutor(
                workspace,
                terminal,
                root);

        using var source =
            new CancellationTokenSource();

        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(
                new DevelopmentAction(
                    DevelopmentActionType.RunBuild,
                    "."),
                source.Token));
    }

    private static string CreateWorkspace()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                "JARVIS",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        return root;
    }

    private sealed class FakeTerminalExecutor : ITerminalExecutor
    {
        private readonly bool _success;
        private readonly string _output;
        private readonly string _error;

        public List<TerminalCommand> Commands { get; } = [];

        public FakeTerminalExecutor(
            bool success = true,
            string output = "",
            string error = "")
        {
            _success = success;
            _output = output;
            _error = error;
        }

        public Task<TerminalResult> ExecuteAsync(
            TerminalCommand command,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Commands.Add(command);

            return Task.FromResult(
                new TerminalResult(
                    _success ? 0 : 1,
                    _output,
                    _error,
                    TimeSpan.Zero,
                    false,
                    false));
        }
    }
}

