using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Finance;

public sealed record DhanFeedInstrument(string ExchangeSegment, int SecurityId);

public sealed record DhanFeedTick(
    string ExchangeSegment,
    int SecurityId,
    double? LastPrice,
    DateTimeOffset? LastTradeTimeUtc,
    double? PreviousClose,
    long? Volume,
    DateTimeOffset ReceivedAtUtc);

public sealed record DhanLivePriceAlert(
    string Id,
    string ExchangeSegment,
    int SecurityId,
    string Label,
    string Condition,
    decimal ThresholdPrice,
    bool Enabled,
    DateTimeOffset? TriggeredAtUtc = null,
    decimal? TriggeredPrice = null);

/// <summary>
/// Parses Dhan v2 little-endian ticker, previous-close and quote packets.
/// </summary>
public static class DhanFeedPacketParser
{
    public static IReadOnlyList<DhanFeedTick> Parse(byte[] data) => Parse(data.AsSpan());

    public static int? TryReadDisconnectCode(byte[] data) => TryReadDisconnectCode(data.AsSpan());

    public static IReadOnlyList<DhanFeedTick> Parse(ReadOnlySpan<byte> data)
    {
        var ticks = new List<DhanFeedTick>();
        var offset = 0;
        while (data.Length - offset >= 8)
        {
            var packet = data[offset..];
            var code = packet[0];
            var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(1, 2));
            var expectedLength = code switch
            {
                2 or 6 => 17,
                4 => 51,
                5 => 12,
                8 => 162,
                50 => 10,
                _ => 0
            };
            var packetLength = declaredLength >= 8 && declaredLength <= packet.Length
                ? declaredLength
                : expectedLength;
            if (packetLength < 8 || packetLength > packet.Length)
                break;

            if (code == 50)
            {
                offset += packetLength;
                continue;
            }

            var segment = SegmentName(packet[3]);
            var securityId = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(4, 4));
            if (segment is not null && securityId > 0)
            {
                double? lastPrice = null;
                double? previousClose = null;
                long? volume = null;
                DateTimeOffset? tradeTime = null;

                if (code == 2 && packetLength >= 17)
                {
                    lastPrice = ReadFloat(packet, 9);
                    tradeTime = ReadEpoch(packet, 13);
                }
                else if (code == 6 && packetLength >= 17)
                {
                    previousClose = ReadFloat(packet, 9);
                }
                else if (code == 4 && packetLength >= 51)
                {
                    lastPrice = ReadFloat(packet, 9);
                    tradeTime = ReadEpoch(packet, 15);
                    volume = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(23, 4));
                }

                if (lastPrice is > 0 || previousClose is > 0)
                    ticks.Add(new DhanFeedTick(segment, securityId, lastPrice, tradeTime,
                        previousClose, volume, DateTimeOffset.UtcNow));
            }

            offset += packetLength;
        }
        return ticks;
    }

    public static int? TryReadDisconnectCode(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        while (data.Length - offset >= 10)
        {
            var packet = data[offset..];
            var code = packet[0];
            var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(1, 2));
            var length = declaredLength >= 10 && declaredLength <= packet.Length ? declaredLength : code == 50 ? 10 : 0;
            if (length < 10 || length > packet.Length) return null;
            if (code == 50)
                return BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(8, 2));
            offset += length;
        }
        return null;
    }

    private static double? ReadFloat(ReadOnlySpan<byte> packet, int offset)
    {
        var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(offset, 4)));
        return float.IsFinite(value) && value > 0 ? value : null;
    }

    private static DateTimeOffset? ReadEpoch(ReadOnlySpan<byte> packet, int offset)
    {
        var epoch = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(offset, 4));
        if (epoch <= 0) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(epoch); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    private static string? SegmentName(byte code) => code switch
    {
        0 => "IDX_I",
        1 => "NSE_EQ",
        2 => "NSE_FNO",
        3 => "NSE_CURRENCY",
        4 => "BSE_EQ",
        5 => "MCX_COMM",
        7 => "BSE_CURRENCY",
        8 => "BSE_FNO",
        _ => null
    };
}

/// <summary>
/// Read-only Dhan v2 WebSocket monitor. It keeps one bounded connection alive,
/// reconnects with backoff, and exposes the latest ticker/quote snapshots.
/// </summary>
public sealed class DhanLiveFeedTool : ITool
{
    private const string FeedBase = "wss://api-feed.dhan.co";
    private const int MaxInstruments = 100;
    private const int MaxMessageBytes = 1_000_000;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly ConcurrentDictionary<string, DhanFeedTick> _latest = new(StringComparer.Ordinal);
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private DhanFeedInstrument[] _instruments = [];
    private string _mode = "ticker";
    private int _connected;
    private string? _lastError;
    private DateTimeOffset? _lastConnectedAt;
    private string? _clientId;
    private string? _accessToken;
    private readonly Func<string?> _clientIdProvider;
    private readonly Func<string?> _accessTokenProvider;
    private readonly string _alertStorePath;
    private readonly Action<string> _notify;
    private readonly SemaphoreSlim _alertGate = new(1, 1);
    private readonly JsonSerializerOptions _alertJsonOptions = new() { WriteIndented = true };
    private List<DhanLivePriceAlert> _alerts = [];
    private bool _alertsLoaded;

    public DhanLiveFeedTool(
        Func<string?>? clientIdProvider = null,
        Func<string?>? accessTokenProvider = null,
        string? alertStorePath = null,
        Action<string>? notify = null)
    {
        _clientIdProvider = clientIdProvider ?? DhanCredentialStore.ReadClientId;
        _accessTokenProvider = accessTokenProvider ?? DhanCredentialStore.ReadAccessToken;
        _alertStorePath = Path.GetFullPath(alertStorePath ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JARVIS", "market", "dhan-live-alerts.json"));
        _notify = notify ?? Console.WriteLine;
    }

    public string Name => "dhan_live_feed";
    public string Description =>
        "Manage a read-only DhanHQ v2 live market-data WebSocket and persistent one-shot price alerts. Actions: start (optional instruments='NSE_EQ:1333,NSE_EQ:2885', optional mode=ticker|quote), status, stop, set_alert (instrument, condition=above|below, price, optional label), alerts, remove_alert (alert_id). Active alert instruments are automatically subscribed when start is called without an instrument list, or added to an explicit list. Resolve symbols first with dhan_instrument_lookup. Up to 100 instruments per connection. Alerts trigger on received live ticks only and never place trades. Requires Windows Credential Manager/environment credentials and an active Dhan Data API subscription.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("action", out var action) || string.IsNullOrWhiteSpace(action))
            return new ToolResult(false, "dhan_live_feed requires action=start|status|stop.");

        try
        {
            return action.Trim().ToLowerInvariant() switch
            {
                "start" => await StartAsync(arguments, cancellationToken),
                "status" => await StatusAsync(arguments, cancellationToken),
                "stop" => await StopAsync(arguments, cancellationToken),
                "set_alert" => await SetAlertAsync(arguments, cancellationToken),
                "alerts" => await ListAlertsAsync(arguments, cancellationToken),
                "remove_alert" => await RemoveAlertAsync(arguments, cancellationToken),
                _ => new ToolResult(false, "Unknown action. Use start, status, stop, set_alert, alerts, or remove_alert.")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ToolResult(false, "Dhan live-alert storage failed safely: " + ex.Message);
        }
    }

    private async Task<ToolResult> StartAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "instruments", "mode"))
            return new ToolResult(false, "start accepts action, optional instruments and optional mode.");

        var activeAlerts = await GetActiveAlertsAsync(token);
        DhanFeedInstrument[] requestedInstruments;
        if (!args.TryGetValue("instruments", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            if (activeAlerts.Count == 0)
                return new ToolResult(false, "start requires instruments, or create one or more live price alerts first.");
            requestedInstruments = activeAlerts
                .Select(alert => new DhanFeedInstrument(alert.ExchangeSegment, alert.SecurityId))
                .Distinct()
                .ToArray();
        }
        else if (!TryParseInstruments(raw, out requestedInstruments, out var error))
        {
            return new ToolResult(false, error);
        }

        var instruments = requestedInstruments
            .Concat(activeAlerts.Select(alert => new DhanFeedInstrument(alert.ExchangeSegment, alert.SecurityId)))
            .Distinct()
            .ToArray();
        if (instruments.Length > MaxInstruments)
            return new ToolResult(false, $"The requested instruments plus active alert instruments exceed the {MaxInstruments}-instrument connection limit.");

        var mode = args.TryGetValue("mode", out var rawMode) ? rawMode.Trim().ToLowerInvariant() : "ticker";
        if (mode is not ("ticker" or "quote"))
            return new ToolResult(false, "mode must be ticker or quote.");

        var clientId = _clientIdProvider()?.Trim();
        var accessToken = _accessTokenProvider()?.Trim();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(accessToken))
            return new ToolResult(false, "Dhan is not connected. Run scripts/Set-DhanCredentials.ps1 or set DHAN_CLIENT_ID and DHAN_ACCESS_TOKEN locally; do not paste credentials into chat.");
        if (clientId.Length > 24 || clientId.Any(ch => !char.IsAsciiDigit(ch)) ||
            accessToken.Length > 8192 || accessToken.Any(char.IsControl))
            return new ToolResult(false, "Dhan credentials have an invalid format. Check the local environment variables.");

        await _lifecycleGate.WaitAsync(token);
        try
        {
            if (_runTask is { IsCompleted: false })
                return new ToolResult(false, "A Dhan live feed is already running. Use status to inspect it or stop before changing subscriptions.");

            _instruments = instruments;
            _mode = mode;
            _clientId = clientId;
            _accessToken = accessToken;
            _latest.Clear();
            _lastError = null;
            _lastConnectedAt = null;
            _runCts?.Dispose();
            _runCts = new CancellationTokenSource();
            var runToken = _runCts.Token;
            _runTask = Task.Run(() => RunAsync(runToken), CancellationToken.None);
            return new ToolResult(true,
                $"Dhan live {mode} feed start requested for {instruments.Length} instrument(s), including {activeAlerts.Count} active alert(s). It will reconnect automatically if the socket drops. Use status to inspect prices and alerts. Data API subscription is required; no orders are placed.");
        }
        finally { _lifecycleGate.Release(); }
    }

    private async Task<ToolResult> StatusAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action"))
            return new ToolResult(false, "status accepts only action.");
        var activeAlerts = await GetActiveAlertsAsync(token);
        await _lifecycleGate.WaitAsync(token);
        try
        {
            var running = _runTask is { IsCompleted: false };
            var connected = Volatile.Read(ref _connected) == 1;
            var lines = new List<string>
            {
                $"Dhan live feed: {(running ? "running" : "stopped")}; socket: {(connected ? "connected" : "disconnected")}.",
                $"Mode: {_mode}; subscribed instruments: {_instruments.Length}; active live alerts: {activeAlerts.Count}.",
                _lastConnectedAt is null ? "No successful connection has been recorded." : $"Last connection: {_lastConnectedAt.Value:yyyy-MM-dd HH:mm:ss} UTC."
            };
            if (!string.IsNullOrWhiteSpace(_lastError)) lines.Add("Last feed note: " + _lastError);
            var ticks = _latest.Values.OrderByDescending(item => item.ReceivedAtUtc).Take(20).ToArray();
            foreach (var tick in ticks)
            {
                var price = tick.LastPrice is null ? "LTP pending" : $"LTP ₹{tick.LastPrice.Value:N2}";
                var time = tick.LastTradeTimeUtc is null ? "" : $" | Trade {tick.LastTradeTimeUtc.Value:HH:mm:ss} UTC";
                var close = tick.PreviousClose is null ? "" : $" | Prev close ₹{tick.PreviousClose.Value:N2}";
                var volume = tick.Volume is null ? "" : $" | Volume {tick.Volume.Value:N0}";
                lines.Add($"{tick.ExchangeSegment}:{tick.SecurityId} | {price}{time}{close}{volume} | Received {tick.ReceivedAtUtc:HH:mm:ss} UTC");
            }
            if (_latest.Count > ticks.Length) lines.Add($"Showing latest {ticks.Length} of {_latest.Count} instruments with received data.");
            if (ticks.Length == 0) lines.Add("No market packets received yet. Check Data API entitlement, token validity and market session.");
            return new ToolResult(true, string.Join(Environment.NewLine, lines));
        }
        finally { _lifecycleGate.Release(); }
    }

    private async Task<ToolResult> StopAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action"))
            return new ToolResult(false, "stop accepts only action.");
        Task? task;
        await _lifecycleGate.WaitAsync(token);
        try
        {
            _runCts?.Cancel();
            task = _runTask;
        }
        finally { _lifecycleGate.Release(); }

        if (task is not null)
        {
            try { await task.WaitAsync(TimeSpan.FromSeconds(8), token); }
            catch (TimeoutException) { return new ToolResult(false, "The Dhan feed is still shutting down; check status again shortly."); }
        }

        await _lifecycleGate.WaitAsync(token);
        try
        {
            _runTask = null;
            _runCts?.Dispose();
            _runCts = null;
            _clientId = null;
            _accessToken = null;
            Volatile.Write(ref _connected, 0);
            return new ToolResult(true, "Dhan live feed stopped. No trading action was performed.");
        }
        finally { _lifecycleGate.Release(); }
    }

    private async Task RunAsync(CancellationToken token)
    {
        var backoffSeconds = 2;
        while (!token.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            try
            {
                var uri = new Uri($"{FeedBase}?version=2&token={Uri.EscapeDataString(_accessToken!)}&clientId={Uri.EscapeDataString(_clientId!)}&authType=2");
                await socket.ConnectAsync(uri, token);
                Volatile.Write(ref _connected, 1);
                var connectedAt = DateTimeOffset.UtcNow;
                _lastConnectedAt = connectedAt;
                _lastError = null;
                await SendSubscriptionsAsync(socket, token);
                await ReceiveLoopAsync(socket, token);
                if (DateTimeOffset.UtcNow - connectedAt >= TimeSpan.FromMinutes(1))
                    backoffSeconds = 2;
                if (!token.IsCancellationRequested)
                {
                    if (_lastError?.StartsWith("Dhan sent a feed disconnect", StringComparison.Ordinal) == true)
                        break;
                    _lastError = "Dhan closed the feed connection; reconnecting.";
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (WebSocketException)
            {
                _lastError = "WebSocket connection or feed transfer failed; reconnecting. Check Data API entitlement and token validity.";
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException)
            {
                _lastError = "Dhan feed encountered a connection or message error; reconnecting.";
            }
            finally
            {
                Volatile.Write(ref _connected, 0);
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        var disconnect = Encoding.UTF8.GetBytes("{\"RequestCode\":12}");
                        await socket.SendAsync(new ArraySegment<byte>(disconnect), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (Exception ex) when (ex is WebSocketException or InvalidOperationException or OperationCanceledException) { }
                }
            }

            if (!token.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                backoffSeconds = Math.Min(backoffSeconds * 2, 30);
            }
        }
        Volatile.Write(ref _connected, 0);
    }

    private async Task SendSubscriptionsAsync(ClientWebSocket socket, CancellationToken token)
    {
        var requestCode = _mode == "quote" ? 17 : 15;
        var payload = JsonSerializer.Serialize(new
        {
            RequestCode = requestCode,
            InstrumentCount = _instruments.Length,
            InstrumentList = _instruments.Select(item => new
            {
                ExchangeSegment = item.ExchangeSegment,
                SecurityId = item.SecurityId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).ToArray()
        });
        var bytes = Encoding.UTF8.GetBytes(payload);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    if (message.Length + result.Count > MaxMessageBytes)
                        throw new WebSocketException("Feed message exceeded the safety limit.");
                    message.Write(buffer, 0, result.Count);
                }
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                var packetBytes = message.ToArray();
                var disconnectCode = DhanFeedPacketParser.TryReadDisconnectCode(packetBytes);
                if (disconnectCode is not null)
                {
                    _lastError = $"Dhan sent a feed disconnect packet (code {disconnectCode.Value}). Check the Data API entitlement, access token and connection limit before restarting.";
                    return;
                }
                foreach (var tick in DhanFeedPacketParser.Parse(packetBytes))
                {
                    var key = $"{tick.ExchangeSegment}:{tick.SecurityId}";
                    _latest.AddOrUpdate(key, tick, (_, previous) => tick with
                    {
                        LastPrice = tick.LastPrice ?? previous.LastPrice,
                        LastTradeTimeUtc = tick.LastTradeTimeUtc ?? previous.LastTradeTimeUtc,
                        PreviousClose = tick.PreviousClose ?? previous.PreviousClose,
                        Volume = tick.Volume ?? previous.Volume
                    });
                    await EvaluateLiveAlertsAsync(tick, token);
                }
            }
        }
    }

    private async Task<ToolResult> SetAlertAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "instrument", "condition", "price", "label") ||
            !args.TryGetValue("instrument", out var rawInstrument) ||
            !args.TryGetValue("condition", out var rawCondition) ||
            !args.TryGetValue("price", out var rawPrice))
            return new ToolResult(false, "set_alert requires instrument=SEGMENT:SECURITY_ID, condition=above|below, and price; optional label.");

        if (!TryParseInstruments(rawInstrument, out var parsed, out var parseError) || parsed.Length != 1)
            return new ToolResult(false, "set_alert requires exactly one resolved instrument. " + (parseError.Length == 0 ? "" : parseError));
        var condition = rawCondition.Trim().ToLowerInvariant();
        if (condition is not ("above" or "below"))
            return new ToolResult(false, "condition must be above or below.");
        if (!decimal.TryParse(rawPrice, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var threshold) ||
            threshold <= 0 || threshold > 100_000_000m)
            return new ToolResult(false, "price must be a positive numeric threshold in rupees, using invariant decimal notation.");

        var label = args.TryGetValue("label", out var rawLabel) && !string.IsNullOrWhiteSpace(rawLabel)
            ? rawLabel.Trim()
            : rawInstrument.Trim();
        if (label.Length > 64 || label.Any(char.IsControl))
            return new ToolResult(false, "label must be 1 to 64 printable characters.");

        await _alertGate.WaitAsync(token);
        try
        {
            await EnsureAlertsLoadedAsync(token);
            if (_alerts.Count >= 100)
                return new ToolResult(false, "The local store is limited to 100 live price alerts. Remove old alerts before adding more.");

            var id = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            _alerts.Add(new DhanLivePriceAlert(id, parsed[0].ExchangeSegment, parsed[0].SecurityId,
                label, condition, threshold, true));
            await SaveAlertsAsync(token);
            return new ToolResult(true,
                $"Saved one-shot live price alert ID={id}: {label} ({rawInstrument.Trim()}) {condition} ₹{threshold.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)}. It triggers only when a new subscribed Dhan tick crosses the threshold. Start the feed to monitor it; no trade will be placed.");
        }
        finally { _alertGate.Release(); }
    }

    private async Task<ToolResult> ListAlertsAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action"))
            return new ToolResult(false, "alerts accepts only action.");
        await _alertGate.WaitAsync(token);
        try
        {
            await EnsureAlertsLoadedAsync(token);
            if (_alerts.Count == 0)
                return new ToolResult(true, "No Dhan live price alerts are saved.");
            var lines = new List<string> { $"Saved Dhan live price alerts: {_alerts.Count}." };
            foreach (var alert in _alerts.OrderByDescending(item => item.Enabled).ThenBy(item => item.Label))
            {
                var state = alert.Enabled
                    ? "ACTIVE"
                    : alert.TriggeredAtUtc is null ? "DISABLED" : $"TRIGGERED at {alert.TriggeredAtUtc.Value:yyyy-MM-dd HH:mm:ss} UTC, LTP ₹{alert.TriggeredPrice:N2}";
                lines.Add($"ID={alert.Id} | {alert.Label} | {alert.ExchangeSegment}:{alert.SecurityId} | {alert.Condition} ₹{alert.ThresholdPrice:N2} | {state}");
            }
            return new ToolResult(true, string.Join(Environment.NewLine, lines));
        }
        finally { _alertGate.Release(); }
    }

    private async Task<ToolResult> RemoveAlertAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "alert_id") ||
            !args.TryGetValue("alert_id", out var id) || string.IsNullOrWhiteSpace(id))
            return new ToolResult(false, "remove_alert requires alert_id from the alerts list.");
        await _alertGate.WaitAsync(token);
        try
        {
            await EnsureAlertsLoadedAsync(token);
            var removed = _alerts.RemoveAll(alert => alert.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return new ToolResult(false, "No live price alert matched that ID.");
            await SaveAlertsAsync(token);
            return new ToolResult(true, $"Removed live price alert {id.Trim()}.");
        }
        finally { _alertGate.Release(); }
    }

    private async Task<List<DhanLivePriceAlert>> GetActiveAlertsAsync(CancellationToken token)
    {
        await _alertGate.WaitAsync(token);
        try
        {
            await EnsureAlertsLoadedAsync(token);
            return _alerts.Where(alert => alert.Enabled).ToList();
        }
        finally { _alertGate.Release(); }
    }

    private async Task EnsureAlertsLoadedAsync(CancellationToken token)
    {
        if (_alertsLoaded) return;
        if (File.Exists(_alertStorePath))
        {
            var info = new FileInfo(_alertStorePath);
            if (info.Length > 512 * 1024)
                throw new InvalidDataException("The live alert file exceeds the 512 KB safety limit.");
            var json = await File.ReadAllTextAsync(_alertStorePath, token);
            _alerts = JsonSerializer.Deserialize<List<DhanLivePriceAlert>>(json) ?? [];
            if (_alerts.Count > 100)
                throw new InvalidDataException("The live alert file contains more than 100 alerts.");
        }
        _alertsLoaded = true;
    }

    private async Task SaveAlertsAsync(CancellationToken token)
    {
        var directory = Path.GetDirectoryName(_alertStorePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _alertStorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(_alerts, _alertJsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, token);
            File.Move(temporaryPath, _alertStorePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private async Task EvaluateLiveAlertsAsync(DhanFeedTick tick, CancellationToken token)
    {
        if (tick.LastPrice is not > 0) return;
        var price = Convert.ToDecimal(tick.LastPrice.Value, System.Globalization.CultureInfo.InvariantCulture);
        List<DhanLivePriceAlert> triggered = [];
        await _alertGate.WaitAsync(token);
        try
        {
            await EnsureAlertsLoadedAsync(token);
            for (var index = 0; index < _alerts.Count; index++)
            {
                var alert = _alerts[index];
                if (!alert.Enabled || alert.ExchangeSegment != tick.ExchangeSegment || alert.SecurityId != tick.SecurityId)
                    continue;
                var crossed = alert.Condition == "above" ? price >= alert.ThresholdPrice : price <= alert.ThresholdPrice;
                if (!crossed) continue;
                var updated = alert with { Enabled = false, TriggeredAtUtc = tick.ReceivedAtUtc, TriggeredPrice = price };
                _alerts[index] = updated;
                triggered.Add(updated);
            }
            if (triggered.Count > 0)
                await SaveAlertsAsync(token);
        }
        finally { _alertGate.Release(); }

        foreach (var alert in triggered)
        {
            var direction = alert.Condition == "above" ? "reached or crossed above" : "reached or crossed below";
            var message = $"JARVIS LIVE PRICE ALERT: {alert.Label} ({alert.ExchangeSegment}:{alert.SecurityId}) {direction} ₹{alert.ThresholdPrice:N2}; received LTP ₹{price:N2} at {tick.ReceivedAtUtc:yyyy-MM-dd HH:mm:ss} UTC. This one-shot alert is now disabled. No order was placed.";
            try { _notify(message); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException) { }
        }
    }

    private static bool TryParseInstruments(string raw, out DhanFeedInstrument[] instruments, out string error)
    {
        instruments = [];
        error = "";
        var segments = new HashSet<string>(StringComparer.Ordinal)
        {
            "NSE_EQ", "BSE_EQ", "NSE_FNO", "BSE_FNO", "NSE_CURRENCY", "BSE_CURRENCY", "MCX_COMM", "IDX_I"
        };
        var items = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (items.Length is < 1 or > MaxInstruments)
        {
            error = $"Provide between 1 and {MaxInstruments} instruments.";
            return false;
        }

        var parsed = new List<DhanFeedInstrument>();
        foreach (var item in items)
        {
            var parts = item.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !segments.Contains(parts[0]) ||
                parts[1].Length is < 1 or > 10 ||
                !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                error = $"Invalid instrument '{item}'. Use a supported segment and positive numeric security ID, for example NSE_EQ:1333.";
                return false;
            }
            if (!parsed.Any(existing => existing.ExchangeSegment == parts[0] && existing.SecurityId == id))
                parsed.Add(new DhanFeedInstrument(parts[0], id));
        }
        instruments = parsed.ToArray();
        if (instruments.Length == 0)
        {
            error = "No valid instruments were supplied.";
            return false;
        }
        return true;
    }

    private static bool HasOnly(Dictionary<string, string> args, params string[] allowed) =>
        args.Keys.All(key => allowed.Contains(key, StringComparer.OrdinalIgnoreCase));
}
