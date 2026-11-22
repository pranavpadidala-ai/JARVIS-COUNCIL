using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Council.AI.Providers;

/// <summary>Answers "is the internet reachable right now?" quickly enough to ask before every AI request.</summary>
public interface IInternetStatus
{
    bool IsOnline();
}

/// <summary>
/// Windows' own "network available" signal first (instant, catches Wi-Fi off and airplane mode), then a
/// 1.5-second TCP connect to well-known public DNS servers, which catches "connected to Wi-Fi but the
/// internet is down". The answer is cached for 15 seconds and dropped when the network changes.
/// </summary>
public sealed class InternetStatus : IInternetStatus
{
    public static readonly InternetStatus Default = new();

    private static readonly (string Host, int Port)[] Probes = { ("1.1.1.1", 443), ("8.8.8.8", 53), ("9.9.9.9", 443) };
    private readonly TimeSpan _cacheFor;
    private readonly Func<bool> _networkAvailable;
    private readonly Func<string, int, TimeSpan, bool> _canConnect;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private (bool Online, DateTimeOffset At)? _cached;

    public InternetStatus(
        TimeSpan? cacheFor = null,
        Func<bool>? networkAvailable = null,
        Func<string, int, TimeSpan, bool>? canConnect = null,
        Func<DateTimeOffset>? clock = null)
    {
        _cacheFor = cacheFor ?? TimeSpan.FromSeconds(15);
        _networkAvailable = networkAvailable ?? NetworkInterface.GetIsNetworkAvailable;
        _canConnect = canConnect ?? TryConnect;
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

        var online = _networkAvailable() && Probes.Any(p => _canConnect(p.Host, p.Port, TimeSpan.FromMilliseconds(1500)));
        lock (_gate) _cached = (online, now);
        return online;
    }

    private static bool TryConnect(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(host, port).Wait(timeout) && client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or AggregateException or ObjectDisposedException)
        {
            return false;
        }
    }
}
