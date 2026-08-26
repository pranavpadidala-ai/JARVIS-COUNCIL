using Council.Dev.Actions;

namespace Council.Dev.Tests;

public class DevelopmentActionTests
{
    [Fact]
    public void Create_file_action_stores_type_target_and_content()
    {
        var action = new DevelopmentAction(
            DevelopmentActionType.CreateFile,
            "src/Test.cs",
            "namespace Test;");

        Assert.Equal(
            DevelopmentActionType.CreateFile,
            action.Type);

        Assert.Equal(
            "src/Test.cs",
            action.Target);

        Assert.Equal(
            "namespace Test;",
            action.Content);
    }

    [Fact]
    public void Read_file_action_can_have_no_content()
    {
        var action = new DevelopmentAction(
            DevelopmentActionType.ReadFile,
            "src/Test.cs");

        Assert.Equal(
            DevelopmentActionType.ReadFile,
            action.Type);

        Assert.Equal(
            "src/Test.cs",
            action.Target);

        Assert.Null(action.Content);
    }

    [Fact]
    public void Run_build_action_uses_target()
    {
        var action = new DevelopmentAction(
            DevelopmentActionType.RunBuild,
            ".");

        Assert.Equal(
            DevelopmentActionType.RunBuild,
            action.Type);

        Assert.Equal(
            ".",
            action.Target);
    }
}
