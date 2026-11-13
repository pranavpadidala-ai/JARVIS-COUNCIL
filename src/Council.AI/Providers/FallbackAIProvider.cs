using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

/// <summary>
/// Tries configured providers in priority order. A provider outage or quota
/// error does not take JARVIS offline while another provider is available.
/// </summary>
public sealed class FallbackAIProvider : IAIProvider
{
    private readonly IReadOnlyList<IAIProvider> _providers;

    public string Name => string.Join(" -> ", _providers.Select(p => p.Name));

    public FallbackAIProvider(IEnumerable<IAIProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();

        if (_providers.Count == 0)
        {
            throw new ArgumentException(
                "At least one AI provider is required.",
                nameof(providers));
        }
    }

    public async Task<AIResponse> AskAsync(
        AIRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var failures = new List<Exception>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await provider.AskAsync(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"{provider.Name} provider failed: {exception.Message}",
                    exception));
            }
        }

        throw new AggregateException(
            "Every configured JARVIS AI provider failed.",
            failures);
    }
}
