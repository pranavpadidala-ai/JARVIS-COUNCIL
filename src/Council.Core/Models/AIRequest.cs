namespace Council.Core.Models;

public sealed record AIRequest(
    IReadOnlyList<AIMessage> Messages,
    string? Model = null,
    double Temperature = 0.2,
    bool JsonMode = false);
