using Council.App.Finance;

namespace Council.Dev.Tests;

public sealed class IndianTraderCliToolTests
{
    [Fact]
    public async Task Rejects_unapproved_mutating_or_broker_actions()
    {
        var tool = new IndianTraderCliTool(Path.Combine(Path.GetTempPath(), "missing-indian-trader", "it.py"));
        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "mcp" });
        Assert.False(result.Success);
        Assert.Contains("read-only", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_unknown_arguments_before_starting_a_process()
    {
        var tool = new IndianTraderCliTool(Path.Combine(Path.GetTempPath(), "missing-indian-trader", "it.py"));
        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["action"] = "quote", ["symbol"] = "NIFTY", ["command"] = "whoami"
        });
        Assert.False(result.Success);
        Assert.Contains("Unsupported argument", result.Message);
    }

    [Fact]
    public async Task Requires_symbol_for_symbol_scoped_actions()
    {
        var tool = new IndianTraderCliTool(Path.Combine(Path.GetTempPath(), "missing-indian-trader", "it.py"));
        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "quote" });
        Assert.False(result.Success);
        Assert.Contains("requires a valid symbol", result.Message);
    }

    [Fact]
    public async Task Reports_missing_optional_submodule_without_crashing()
    {
        var tool = new IndianTraderCliTool(Path.Combine(Path.GetTempPath(), "missing-indian-trader", "it.py"));
        var result = await tool.ExecuteAsync(new Dictionary<string, string> { ["action"] = "pulse" });
        Assert.False(result.Success);
        Assert.Contains("not initialized", result.Message);
    }
}
