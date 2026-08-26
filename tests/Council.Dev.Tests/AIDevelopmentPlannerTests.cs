using Council.Core.Models;
using Council.Core.Providers;
using Council.Dev.Actions;
using Council.Dev.Planning;
using Council.Dev.Tasks;

namespace Council.Dev.Tests;

public class AIDevelopmentPlannerTests
{
    [Fact]
    public async Task Valid_ai_json_returns_structured_actions()
    {
        var provider = new FakeAIProvider("""
        {
          "summary": "Add automated testing",
          "actions": [
            {
              "type": "ReadFile",
              "target": "tests/Council.Dev.Tests",
              "content": null
            },
            {
              "type": "ModifyFile",
              "target": "tests/Council.Dev.Tests/AIDevelopmentPlannerTests.cs",
              "content": "new test content"
            },
            {
              "type": "RunTests",
              "target": ".",
              "content": null
            }
          ]
        }
        """);

        var planner = new AIDevelopmentPlanner(
            provider,
            new RepositoryContextProvider(Directory.GetCurrentDirectory()));

        var task = new DevelopmentTask
        {
            Title = "Add automated testing",
            Description = "Add tests for the development planner."
        };

        var result = await planner.CreatePlanAsync(task);

        Assert.True(result.Success);

        Assert.Equal(
            "Add automated testing",
            result.Summary);

        Assert.Equal(3, result.Actions.Count);

        Assert.Equal(
            DevelopmentActionType.ReadFile,
            result.Actions[0].Type);

        Assert.Equal(
            "tests/Council.Dev.Tests",
            result.Actions[0].Target);

        Assert.Equal(
            DevelopmentActionType.ModifyFile,
            result.Actions[1].Type);

        Assert.Equal(
            "new test content",
            result.Actions[1].Content);

        Assert.Equal(
            DevelopmentActionType.RunTests,
            result.Actions[2].Type);

        Assert.Equal(3, result.Steps.Count);
        Assert.Equal(
            "tests/Council.Dev.Tests",
            result.Steps[0]);

        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Unsupported_action_type_returns_failed_plan()
    {
        var provider = new FakeAIProvider("""
        {
          "summary": "Unsafe plan",
          "actions": [
            {
              "type": "ExecuteShell",
              "target": "format C:",
              "content": null
            }
          ]
        }
        """);

        var planner = new AIDevelopmentPlanner(
            provider,
            new RepositoryContextProvider(Directory.GetCurrentDirectory()));

        var result = await planner.CreatePlanAsync(
            CreateTask());

        Assert.False(result.Success);
        Assert.Contains(
            "unsupported action type",
            result.Summary,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_json_returns_failed_plan()
    {
        var provider = new FakeAIProvider(
            "This is not valid JSON.");

        var planner = new AIDevelopmentPlanner(
            provider,
            new RepositoryContextProvider(Directory.GetCurrentDirectory()));

        var result = await planner.CreatePlanAsync(CreateTask());

        Assert.False(result.Success);
        Assert.Equal(
            "AI returned invalid plan JSON.",
            result.Summary);
    }

    [Fact]
    public async Task Empty_ai_response_returns_failed_plan()
    {
        var provider = new FakeAIProvider("   ");

        var planner = new AIDevelopmentPlanner(
            provider,
            new RepositoryContextProvider(Directory.GetCurrentDirectory()));

        var result = await planner.CreatePlanAsync(
            CreateTask());

        Assert.False(result.Success);
        Assert.Equal(
            "AI returned an empty development plan.",
            result.Summary);
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        var provider = new FakeAIProvider(
            """
            {
              "summary": "Test",
              "actions": [
                {
                  "type": "RunTests",
                  "target": ".",
                  "content": null
                }
              ]
            }
            """);

        var planner = new AIDevelopmentPlanner(
            provider,
            new RepositoryContextProvider(Directory.GetCurrentDirectory()));

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => planner.CreatePlanAsync(
                CreateTask(),
                cancellationSource.Token));
    }

    [Fact]
    public async Task Null_task_is_rejected()
    {
        var provider = new FakeAIProvider(
            """
            {
              "summary": "Test",
              "actions": [
                {
                  "type": "RunTests",
                  "target": ".",
                  "content": null
                }
              ]
            }
            """);

        var planner = new AIDevelopmentPlanner(
            provider,
            new RepositoryContextProvider(Directory.GetCurrentDirectory()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => planner.CreatePlanAsync(null!));
    }

    private static DevelopmentTask CreateTask()
    {
        return new DevelopmentTask
        {
            Title = "Test development task",
            Description = "Test development task description"
        };
    }

    private sealed class FakeAIProvider : IAIProvider
    {
        private readonly string _response;

        public string Name => "Fake";

        public int CallCount { get; private set; }

        public FakeAIProvider(string response)
        {
            _response = response;
        }

        public Task<AIResponse> AskAsync(
            AIRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            return Task.FromResult(
                new AIResponse(
                    Provider: Name,
                    Content: _response,
                    Model: "fake-model"));
        }
    }
}

