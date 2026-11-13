using System.Text.RegularExpressions;
using Council.AI.Providers;
using Council.App;

namespace Council.Dev.Tests;

public sealed class AutonomousDevelopmentToolTests
{
    [Fact]
    public async Task CreateTaskPersistsAndCanBeListedByANewToolInstance()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisDevToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var taskPath = Path.Combine(root, "tasks.json");
        try
        {
            var firstTool = new AutonomousDevelopmentTool(new MockAIProvider(), root, taskPath);
            var created = await firstTool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "create_task",
                ["title"] = "Add a settings screen",
                ["description"] = "Create a settings screen with provider configuration."
            });

            Assert.True(created.Success, created.Message);
            var idMatch = Regex.Match(created.Message, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            Assert.True(idMatch.Success, created.Message);

            var secondTool = new AutonomousDevelopmentTool(new MockAIProvider(), root, taskPath);
            var listed = await secondTool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "list_tasks"
            });

            Assert.True(listed.Success, listed.Message);
            Assert.Contains(idMatch.Value, listed.Message);
            Assert.Contains("Add a settings screen", listed.Message);
            Assert.Contains("Pending", listed.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TaskCreationRequiresTitleAndBoundsInput()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisDevToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tool = new AutonomousDevelopmentTool(
                new MockAIProvider(), root, Path.Combine(root, "tasks.json"));

            var missingTitle = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "create_task",
                ["description"] = "No title supplied."
            });
            Assert.False(missingTitle.Success);

            var longTitle = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "create_task",
                ["title"] = new string('x', 201)
            });
            Assert.False(longTitle.Success);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptTaskStoreIsReportedWithoutBeingOverwritten()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisDevToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var taskPath = Path.Combine(root, "tasks.json");
        const string corruptContent = "{ this is not valid JSON";
        try
        {
            await File.WriteAllTextAsync(taskPath, corruptContent);
            var tool = new AutonomousDevelopmentTool(new MockAIProvider(), root, taskPath);
            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "list_tasks"
            });

            Assert.False(result.Success);
            Assert.Contains("Development task operation failed safely", result.Message);
            Assert.Equal(corruptContent, await File.ReadAllTextAsync(taskPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunTaskRequiresValidTaskId()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisDevToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tool = new AutonomousDevelopmentTool(
                new MockAIProvider(), root, Path.Combine(root, "tasks.json"));
            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "run_task",
                ["task_id"] = "not-a-guid"
            });
            Assert.False(result.Success);
            Assert.Contains("valid task GUID", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ForegroundRun_IsBlockedWhenBackgroundAgentAlreadyClaimedApproval()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisDevToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var taskPath = Path.Combine(root, "tasks.json");
        try
        {
            var manager = new Council.Dev.Tasks.TaskManager(new Council.Dev.Tasks.TaskStore(taskPath));
            await manager.InitializeAsync();
            var task = await manager.CreateAsync("Claimed task", "Must not execute twice.");
            Assert.True(await manager.ApproveAsync(task.Id));
            Assert.NotNull(await manager.ClaimNextApprovedTaskAsync());

            var tool = new AutonomousDevelopmentTool(new MockAIProvider(), root, taskPath);
            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "run_task",
                ["task_id"] = task.Id.ToString()
            });

            Assert.False(result.Success);
            Assert.Contains("already been claimed", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
