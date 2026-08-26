namespace Council.Dev.Actions;

public sealed record DevelopmentAction(
    DevelopmentActionType Type,
    string Target,
    string? Content = null);
