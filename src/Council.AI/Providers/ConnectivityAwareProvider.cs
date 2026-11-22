using Council.Core.Models;
using Council.Core.Providers;

namespace Council.AI.Providers;

/// <summary>
/// The owner's rule: when the laptop is online, use the cloud brain (falling back to local only if every
/// cloud provider fails); when there is no internet, go straight to the local brain without waiting for
/// cloud timeouts. A cloud failure caused by the connection dropping mid-request also falls back locally.
/// </summary>
public sealed class ConnectivityAwareProvider : IAIProvider
{
    private readonly IReadOnlyList<IAIProvider> _cloud;
    private readonly IAIProvider _local;
    private readonly IInternetStatus _internet;
    private readonly TimeSpan _retryDelay;

    public ConnectivityAwareProvider(IEnumerable<IAIProvider> cloud, IAIProvider local, IInternetStatus? internet = null, TimeSpan? cloudRetryDelay = null)
    {
        _retryDelay = cloudRetryDelay ?? TimeSpan.FromSeconds(4);
        _cloud = cloud.ToArray();
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _internet = internet ?? InternetStatus.Default;
        if (_cloud.Count == 0) throw new ArgumentException("At least one cloud provider is required.", nameof(cloud));
    }

    public string Name => $"Auto (online: {string.Join(" -> ", _cloud.Select(p => p.Name))}; offline: {_local.Name})";

    /// <summary>"cloud" or "local": which brain answered last, for the status line.</summary>
    public string? LastRoute { get; private set; }

    public async Task<AIResponse> AskAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_internet.IsOnline())
        {
            LastRoute = "local";
            return await _local.AskAsync(request, cancellationToken);
        }

        var failures = new List<Exception>();
        foreach (var provider in _cloud)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await provider.AskAsync(request, cancellationToken);
                LastRoute = "cloud";
                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException($"{provider.Name} provider failed: {ex.Message}", ex));
            }
        }

        // Still online? The cloud gateway may just be starting up (JARVIS launches it on open).
        // One retry after a short pause is far quicker than a CPU-only local answer.
        if (_retryDelay > TimeSpan.Zero && _internet.IsOnline())
        {
            await Task.Delay(_retryDelay, cancellationToken);
            try
            {
                var response = await _cloud[0].AskAsync(request, cancellationToken);
                LastRoute = "cloud";
                return response;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException($"{_cloud[0].Name} retry failed: {ex.Message}", ex));
            }
        }

        try
        {
            LastRoute = "local";
            return await _local.AskAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures.Add(new InvalidOperationException($"{_local.Name} provider failed: {ex.Message}", ex));
            throw new AggregateException("Every configured JARVIS AI provider failed.", failures);
        }
    }
}
