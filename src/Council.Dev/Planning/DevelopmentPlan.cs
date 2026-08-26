using Council.Dev.Actions;

namespace Council.Dev.Planning;

public sealed record DevelopmentPlan(
    bool Success,
    string Summary,
    IReadOnlyList<string> Steps,
    IReadOnlyList<DevelopmentAction> Actions)
{
    public static DevelopmentPlan Created(
        string summary,
        IReadOnlyList<string> steps,
        IReadOnlyList<DevelopmentAction>? actions = null)
        => new(
            true,
            summary,
            steps,
            actions ?? steps
                .Select(step =>
                    new DevelopmentAction(
                        DevelopmentActionType.ReadFile,
                        step))
                .ToList());

    public static DevelopmentPlan Created(
        string summary,
        IReadOnlyList<DevelopmentAction> actions)
        => new(
            true,
            summary,
            actions
                .Select(action =>
                    action.Target)
                .ToList(),
            actions);

    public static DevelopmentPlan Failed(
        string message)
        => new(
            false,
            message,
            [],
            []);
}
