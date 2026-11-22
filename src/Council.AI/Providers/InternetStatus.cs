using System.Net;
using System.Net.NetworkInformation;

namespace Council.AI.Providers;

/// <summary>Answers "is the internet reachable right now?" quickly enough to ask before every AI request.</summary>
public interface IInternetStatus
{
    bool IsOnline();
}

/// <summary>
/// Windows' own "network available" signal first (instant, catches Wi-Fi off and airplane mode), then the
/// same kind of check Windows uses for its "No internet" icon: a tiny web request to a connectivity-test
/// page (Microsoft's, then Google's), through the system proxy, with a 2.5-second limit. Raw connections to
/// public IP addresses are not used because many routers and firewalls block them while the web works.
/// The answer is cached for 15 seconds and dropped when the network changes.
/// </summary>
public sealed class InternetStatus : IInternetStatus
{
    public static readonly InternetStatus Default = new();

    private static readonly Uri[] Probes =
    {
        new("http://www.msftconnecttest.com/connecttest.txt"),
        new("https://www.google.com/generate_204"),
        new("https://cloudflare.com/cdn-cgi/trace")
    };

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = true })
    {
        Timeout = TimeSpan.FromSeconds(2.5)
    };

    private readonly TimeSpan _cacheFor;
    private readonly Func<bool> _networkAvailable;
    private readonly Func<Uri, bool> _probe;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private (bool Online, DateTimeOffset At)? _cached;

    public InternetStatus(
        TimeSpan? cacheFor = null,
        Func<bool>? networkAvailable = null,
        Func<Uri, bool>? probe = null,
        Func<DateTimeOffset>? clock = null)
    {
        _cacheFor = cacheFor ?? TimeSpan.FromSeconds(15);
        _networkAvailable = networkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
        _probe = probe ?? Reachable;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (networkAvailable is null)
        {
            try
            {
                NetworkChange.NetworkAvailabilityChanged += (_, _) => Invalidate();
                NetworkChange.NetworkAddressChanged += (_, _) => Invalidate();
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or NetworkInformationException) { }
        }
    }

    public void Invalidate()
    {
        lock (_gate) _cached = null;
    }

    public bool IsOnline()
    {
        var now = _clock();
        lock (_gate)
        {
            if (_cached is { } c && now - c.At < _cacheFor) return c.Online;
        }

        var online = _networkAvailable() && Probes.Any(_probe);
        lock (_gate) _cached = (online, now);
        return online;
    }

    /// <summary>
    /// Online only for the expected answer: a Wi-Fi login page (hotel, college) redirects instead,
    /// which correctly counts as offline.
    /// </summary>
    private static bool Reachable(Uri url)
    {
        try
        {
            using var response = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return false;
        }
    }
}
