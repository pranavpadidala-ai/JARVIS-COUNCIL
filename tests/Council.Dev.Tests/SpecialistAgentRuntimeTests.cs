using Council.App.Agents;
using Council.Core.Models;
using Council.Core.Providers;
using Council.Execution;
using Council.Execution.Tools;

namespace Council.Dev.Tests;

public sealed class SpecialistAgentRuntimeTests
{
    [Fact]
    public void RegistryContainsCoreAndDomainSpecialistsWithBoundedToolPermissions()
    {
        var runtime = new SpecialistAgentRuntime(new QueueProvider(), new ToolRegistry());

        var agents = runtime.ListAgents().ToDictionary(agent => agent.Name);

        Assert.Contains("planner", agents.Keys);
        Assert.Contains("architect", agents.Keys);
        Assert.Contains("tdd-guide", agents.Keys);
        Assert.Contains("code-reviewer", agents.Keys);
        Assert.Contains("security-reviewer", agents.Keys);
        Assert.Contains("build-error-resolver", agents.Keys);
        Assert.Contains("researcher", agents.Keys);
        Assert.Contains("doc-updater", agents.Keys);
        Assert.Contains("structural-engineering", agents.Keys);
        Assert.DoesNotContain("create_file", agents["code-reviewer"].AllowedTools);
        Assert.DoesNotContain("development_tasks", agents["security-reviewer"].AllowedTools);
        Assert.Contains("web_search", agents["researcher"].AllowedTools);
    }

    [Fact]
    public async Task RunUsesFreshIsolatedContextAndReturnsSpecialistOutput()
    {
        var provider = new QueueProvider("""{"type":"message","content":"Found one correctness issue in the supplied method."}""");
        var runtime = new SpecialistAgentRuntime(provider, new ToolRegistry());

        var result = await runtime.RunAsync("code-reviewer", "Review this method.", "public int Add(int a, int b) => a - b;");

        Assert.True(result.Success);
        Assert.Equal("Found one correctness issue in the supplied method.", result.Content);
        Assert.Equal("FakeProvider", result.Provider);
        var request = Assert.Single(provider.Requests);
        Assert.Equal(2, request.Messages.Count);
        Assert.Contains("code-reviewer", request.Messages[0].Content);
        Assert.Contains("Review this method.", request.Messages[1].Content);
        Assert.Contains("public int Add", request.Messages[1].Content);
    }

    [Fact]
    public async Task AgentCanCallOnlyItsExplicitlyAllowedReadOnlyTools()
    {
        var provider = new QueueProvider(
            """{"type":"tool_call","name":"read_file","arguments":{"path":"src/example.cs"}}""",
            """{"type":"message","content":"Reviewed the file contents."}""");
        var tools = new ToolRegistry();
        var read = new RecordingTool("read_file", new ToolResult(true, "class Example {}"));
        tools.Register(read);
        var runtime = new SpecialistAgentRuntime(provider, tools);

        var result = await runtime.RunAsync("code-reviewer", "Review the implementation.");

        Assert.True(result.Success);
        Assert.Equal("Reviewed the file contents.", result.Content);
        Assert.Equal(1, read.Calls);
        Assert.Equal("src/example.cs", read.LastArguments!["path"]);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Contains("SUCCESS: class Example {}", provider.Requests[1].Messages.Last().Content);
    }

    [Fact]
    public async Task AgentCannotInvokeWriteToolsEvenWhenTheyAreRegistered()
    {
        var provider = new QueueProvider(
            """{"type":"tool_call","name":"create_file","arguments":{"path":"bad.txt","content":"unsafe"}}""",
            """{"type":"message","content":"I was denied write access and did not change files."}""");
        var tools = new ToolRegistry();
        var write = new RecordingTool("create_file", new ToolResult(true, "created"));
        tools.Register(write);
        var runtime = new SpecialistAgentRuntime(provider, tools);

        var result = await runtime.RunAsync("security-reviewer", "Inspect the code.");

        Assert.True(result.Success);
        Assert.Equal(0, write.Calls);
        Assert.Contains("denied", provider.Requests[1].Messages.Last().Content.ToLowerInvariant());
    }

    [Fact]
    public async Task AgentRejectsOversizedToolArgumentsBeforeExecution()
    {
        var provider = new QueueProvider(
            """{"type":"tool_call","name":"read_file","arguments":{"path":"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"}}""",
            """{"type":"message","content":"The oversized request was rejected."}""");
        var tools = new ToolRegistry();
        var read = new RecordingTool("read_file", new ToolResult(true, "should not run"));
        tools.Register(read);
        var runtime = new SpecialistAgentRuntime(provider, tools);

        var result = await runtime.RunAsync("code-reviewer", "Review the file.");
        Assert.True(result.Success);
        Assert.Equal(0, read.Calls);
        Assert.Contains("per-value limit", provider.Requests[1].Messages.Last().Content);
    }

    [Fact]
    public async Task TeamKeepsSuccessfulResultsWhenOneAgentProviderCallFails()
    {
        var runtime = new SpecialistAgentRuntime(new SelectiveFailureProvider(), new ToolRegistry());

        var results = await runtime.RunTeamAsync(
            ["code-reviewer", "architect"], "Review the proposed design.");

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.Contains("failed independently", results[0].Content);
        Assert.True(results[1].Success);
        Assert.Equal("Architect result.", results[1].Content);
    }

    [Fact]
    public async Task TeamRejectsMoreThanFourAgents()
    {
        var runtime = new SpecialistAgentRuntime(new QueueProvider(), new ToolRegistry());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runtime.RunTeamAsync(
                ["planner", "architect", "tdd-guide", "code-reviewer", "security-reviewer"],
                "Review a change."));
    }

    private sealed class QueueProvider : IAIProvider
    {
        private readonly Queue<string> _responses;
        public QueueProvider(params string[] responses) => _responses = new Queue<string>(responses);
        public string Name => "FakeProvider";
        public List<AIRequest> Requests { get; } = [];

        public Task<AIResponse> AskAsync(AIRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (_responses.Count == 0)
                throw new InvalidOperationException("No fake response was queued.");
            return Task.FromResult(new AIResponse(Name, _responses.Dequeue(), "fake-model"));
        }
    }

    private sealed class SelectiveFailureProvider : IAIProvider
    {
        public string Name => "SelectiveFakeProvider";

        public Task<AIResponse> AskAsync(AIRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prompt = request.Messages[0].Content;
            if (prompt.Contains("code-reviewer", StringComparison.Ordinal))
                throw new HttpRequestException("simulated provider failure");

            return Task.FromResult(new AIResponse(
                Name,
                """{"type":"message","content":"Architect result."}""",
                "fake-model"));
        }
    }

    private sealed class RecordingTool : ITool
    {
        private readonly string _name;
        private readonly ToolResult _result;
        public RecordingTool(string name, ToolResult result) { _name = name; _result = result; }
        public string Name => _name;
        public string Description => "Test tool";
        public int Calls { get; private set; }
        public Dictionary<string, string>? LastArguments { get; private set; }

        public Task<ToolResult> ExecuteAsync(
            Dictionary<string, string> arguments,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastArguments = new Dictionary<string, string>(arguments);
            return Task.FromResult(_result);
        }
    }
}
