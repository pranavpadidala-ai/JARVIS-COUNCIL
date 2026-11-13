using System.Globalization;

namespace Council.App.Finance;

/// <summary>Daily 09:00 IST EOD portfolio and price-alert check while JARVIS is running.</summary>
public sealed class IndianMarketMonitorService : IAsyncDisposable
{
    private readonly IndianMarketTool _market;
    private readonly Action<string> _notify;
    private readonly TimeOnly _runAt;
    private readonly string _statePath;
    private readonly TimeZoneInfo _indiaTimeZone;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public IndianMarketMonitorService(IndianMarketTool market, Action<string>? notify = null, TimeOnly? runAt = null, string? statePath = null)
    {
        _market = market;
        _notify = notify ?? (message => Console.WriteLine(Environment.NewLine + message + Environment.NewLine));
        _runAt = runAt ?? new TimeOnly(9, 0);
        _statePath = Path.GetFullPath(statePath ??
            Environment.GetEnvironmentVariable("JARVIS_MARKET_MONITOR_STATE_PATH") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS", "market", "monitor-state.txt"));
        _indiaTimeZone = ResolveIndiaTimeZone();
    }

    public void Start()
    {
        if (_loop is not null) return;
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    public static DateTimeOffset GetNextScheduledRun(DateTimeOffset indiaLocalNow, TimeOnly runAt)
    {
        var date = DateOnly.FromDateTime(indiaLocalNow.DateTime);
        var candidate = new DateTimeOffset(date.ToDateTime(runAt), indiaLocalNow.Offset);
        if (candidate <= indiaLocalNow) date = date.AddDays(1);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(1);
        return new DateTimeOffset(date.ToDateTime(runAt), indiaLocalNow.Offset);
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _indiaTimeZone);
            var today = DateOnly.FromDateTime(now.DateTime);
            var lastRun = await LoadLastRunDateAsync(token);
            if (today.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) &&
                now.TimeOfDay >= _runAt.ToTimeSpan() && lastRun != today)
            {
                await RunDailyCheckAsync(today, token);
                continue;
            }
            var nextRun = GetNextScheduledRun(now, _runAt);
            await Task.Delay(nextRun - now, token);
        }
    }

    private async Task RunDailyCheckAsync(DateOnly date, CancellationToken token)
    {
        try
        {
            if (!await _market.HasMonitorItemsAsync(token))
            {
                await SaveLastRunDateAsync(date, token);
                return;
            }
            var portfolio = await _market.ExecuteAsync(new Dictionary<string, string> { ["action"] = "portfolio" }, token);
            var alerts = await _market.ExecuteAsync(new Dictionary<string, string> { ["action"] = "check_alerts" }, token);
            _notify($"""
                JARVIS DAILY INDIAN MARKET MONITOR — {date:yyyy-MM-dd} IST
                Data: latest available NSE/BSE daily close (normally previous trading session), not live/intraday.

                {portfolio.Message}

                {alerts.Message}
                """);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _notify($"JARVIS daily market monitor could not complete on {date:yyyy-MM-dd}: {ex.Message}");
        }
        await SaveLastRunDateAsync(date, token);
    }

    private async Task<DateOnly?> LoadLastRunDateAsync(CancellationToken token)
    {
        if (!File.Exists(_statePath)) return null;
        var value = await File.ReadAllTextAsync(_statePath, token);
        return DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    private async Task SaveLastRunDateAsync(DateOnly date, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (string.IsNullOrWhiteSpace(directory)) throw new IOException("Could not resolve market-monitor state directory.");
        Directory.CreateDirectory(directory);
        var temporary = _statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), token);
            File.Move(temporary, _statePath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static TimeZoneInfo ResolveIndiaTimeZone()
    {
        foreach (var id in new[] { "India Standard Time", "Asia/Kolkata" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Utc;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null)
        {
            try { await _loop; }
            catch (OperationCanceledException) { }
        }
        _stop.Dispose();
    }
}
