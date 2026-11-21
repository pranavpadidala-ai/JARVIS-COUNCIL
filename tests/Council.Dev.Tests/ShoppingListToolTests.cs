using Council.App.Shopping;
using Xunit;

namespace Council.Dev.Tests;

public sealed class ShoppingListToolTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "jarvis-shop-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public async Task AddsMarksAndShowsItems()
    {
        var tool = new ShoppingListTool(_path);
        await tool.ExecuteAsync(new() { ["action"] = "add", ["name"] = "Milk", ["quantity"] = "2 L" });
        await tool.ExecuteAsync(new() { ["action"] = "add", ["name"] = "Vernier caliper", ["list"] = "lab", ["budget"] = "₹1,500" });
        var bought = await tool.ExecuteAsync(new() { ["action"] = "bought", ["name"] = "milk" });
        Assert.True(bought.Success);

        var show = await tool.ExecuteAsync(new() { ["action"] = "show" });
        Assert.Contains("[x] Milk x 2 L", show.Message);
        Assert.Contains("Budget still to spend: ₹1500", show.Message);

        var cleared = await tool.ExecuteAsync(new() { ["action"] = "clear_bought" });
        Assert.Contains("Cleared 1", cleared.Message);
        Assert.DoesNotContain("Milk", (await tool.ExecuteAsync(new() { ["action"] = "show" })).Message);
    }

    [Fact]
    public async Task RejectsUnknownActionsAndMissingItems()
    {
        var tool = new ShoppingListTool(_path);
        Assert.False((await tool.ExecuteAsync(new() { ["action"] = "checkout" })).Success);
        Assert.False((await tool.ExecuteAsync(new() { ["action"] = "remove", ["name"] = "nothing" })).Success);
        Assert.Contains("Never place an order", tool.Description);
    }
}
