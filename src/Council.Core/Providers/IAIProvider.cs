using Council.Core.Models;

namespace Council.Core.Providers;

public interface IAIProvider
{
    string Name { get; }

    Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default);
}
