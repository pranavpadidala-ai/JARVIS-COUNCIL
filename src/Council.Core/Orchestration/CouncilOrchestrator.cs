using Council.Core.Models;
using Council.Core.Providers;

namespace Council.Core.Orchestration;

public sealed class CouncilOrchestrator
{
    private readonly IReadOnlyList<IAIProvider> _providers;

    public CouncilOrchestrator(IEnumerable<IAIProvider> providers)
    {
        _providers = providers.ToList();

        if (_providers.Count == 0)
        {
            throw new ArgumentException(
                "At least one AI provider is required.",
                nameof(providers));
        }
    }

    public async Task<IReadOnlyList<AIResponse>> AskAllAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        var tasks = _providers.Select(provider =>
            provider.AskAsync(request, cancellationToken));

        return await Task.WhenAll(tasks);
    }
}
