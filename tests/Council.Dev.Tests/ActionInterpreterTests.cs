using Council.Dev.Actions;

namespace Council.Dev.Tests;

public class ActionInterpreterTests
{
    private readonly ActionInterpreter _interpreter = new();

    [Fact]
    public void Build_step_becomes_build_action()
    {
        var actions = _interpreter.Interpret(
            [
                "Run dotnet build"
            ]);

        Assert.Single(actions);
        Assert.Equal(
            DevelopmentActionType.RunBuild,
            actions[0].Type);
    }

    [Fact]
    public void Test_step_becomes_test_action()
    {
        var actions = _interpreter.Interpret(
            [
                "Run dotnet test"
            ]);

        Assert.Single(actions);
        Assert.Equal(
            DevelopmentActionType.RunTests,
            actions[0].Type);
    }

    [Fact]
    public void Create_step_becomes_create_file_action()
    {
        var actions = _interpreter.Interpret(
            [
                "Create src/Test.cs"
            ]);

        Assert.Single(actions);

        Assert.Equal(
            DevelopmentActionType.CreateFile,
            actions[0].Type);

        Assert.Equal(
            "src/Test.cs",
            actions[0].Target);
    }

    [Fact]
    public void Modify_step_becomes_modify_file_action()
    {
        var actions = _interpreter.Interpret(
            [
                "Modify src/Test.cs"
            ]);

        Assert.Single(actions);

        Assert.Equal(
            DevelopmentActionType.ModifyFile,
            actions[0].Type);

        Assert.Equal(
            "src/Test.cs",
            actions[0].Target);
    }

    [Fact]
    public void Delete_step_becomes_delete_file_action()
    {
        var actions = _interpreter.Interpret(
            [
                "Delete src/Test.cs"
            ]);

        Assert.Single(actions);

        Assert.Equal(
            DevelopmentActionType.DeleteFile,
            actions[0].Type);
    }

    [Fact]
    public void Multiple_steps_preserve_order()
    {
        var actions = _interpreter.Interpret(
            [
                "Read src/Test.cs",
                "Modify src/Test.cs",
                "Run dotnet build",
                "Run dotnet test"
            ]);

        Assert.Equal(4, actions.Count);

        Assert.Equal(
            DevelopmentActionType.ReadFile,
            actions[0].Type);

        Assert.Equal(
            DevelopmentActionType.ModifyFile,
            actions[1].Type);

        Assert.Equal(
            DevelopmentActionType.RunBuild,
            actions[2].Type);

        Assert.Equal(
            DevelopmentActionType.RunTests,
            actions[3].Type);
    }

    [Fact]
    public void Blank_steps_are_ignored()
    {
        var actions = _interpreter.Interpret(
            [
                "",
                "   ",
                "Run dotnet build"
            ]);

        Assert.Single(actions);

        Assert.Equal(
            DevelopmentActionType.RunBuild,
            actions[0].Type);
    }

    [Fact]
    public void Null_steps_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => _interpreter.Interpret(null!));
    }
}