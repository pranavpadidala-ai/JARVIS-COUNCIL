namespace Council.Core.Models;

public sealed record AIResponse(
    string Provider,
    string Content,
    string? Model = null,
    TimeSpan? Duration = null);
