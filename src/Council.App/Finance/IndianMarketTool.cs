using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Council.Execution.Tools;

namespace Council.App.Finance;

/// <summary>
/// Private NSE/BSE equity watchlist and portfolio monitor. Uses daily EOD OHLCV only;
/// it is not an intraday feed and has no broker/order capability.
/// </summary>
public sealed class IndianMarketTool : ITool
{
    private const string ApiBase = "https://api.tejhq.dev";
    private const int MaxTrackedSymbols = 30;
    private const int MaxPriceAlerts = 30;
    private const int MaxResponseCharacters = 1_000_000;
    private static readonly Regex SymbolPattern = new("^[A-Z0-9][A-Z0-9&-]{0,29}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly HttpClient _http;
    private readonly string _watchlistPath;
    private readonly SemaphoreSlim _storeGate = new(1, 1);

    public IndianMarketTool(HttpClient? httpClient = null, string? watchlistPath = null)
    {
        _http = httpClient ?? SharedClient;
        _watchlistPath = Path.GetFullPath(watchlistPath ??
            Environment.GetEnvironmentVariable("JARVIS_MARKET_WATCHLIST_PATH") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "JARVIS", "market", "watchlist.json"));
    }

    public string Name => "indian_market";

    public string Description =>
        "Private Indian NSE/BSE equity watchlist and holdings monitor using TejHQ daily end-of-day (EOD) OHLCV. " +
        "Actions: set_holding (symbol, quantity, average_buy_price, optional exchange), add_watch (symbol, optional exchange), " +
        "set_alert (symbol, condition=above|below, price, optional exchange), alerts, remove_alert (alert_id), check_alerts, " +
        "remove (symbol, optional exchange), list, quote, portfolio. " +
        "EOD data is normally ready the next morning IST, not live/intraday. No broker login, no order placement, no trading. " +
        "Only save holdings values explicitly supplied by the user. All arguments are strings.";

    public async Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("action", out var action) || string.IsNullOrWhiteSpace(action))
            return new ToolResult(false, "indian_market requires an action: set_holding, add_watch, set_alert, alerts, remove_alert, check_alerts, remove, list, quote, or portfolio.");

        try
        {
            return action.Trim().ToLowerInvariant() switch
            {
                "set_holding" => await SetHoldingAsync(arguments, cancellationToken),
                "add_watch" => await AddWatchAsync(arguments, cancellationToken),
                "remove" => await RemoveAsync(arguments, cancellationToken),
                "list" => await ListAsync(arguments, cancellationToken),
                "quote" => await QuoteAsync(arguments, cancellationToken),
                "portfolio" => await PortfolioAsync(arguments, cancellationToken),
                "set_alert" => await SetAlertAsync(arguments, cancellationToken),
                "alerts" => await AlertsAsync(arguments, cancellationToken),
                "remove_alert" => await RemoveAlertAsync(arguments, cancellationToken),
                "check_alerts" => await CheckAlertsAsync(arguments, cancellationToken),
                _ => new ToolResult(false, "Unknown action. Use set_holding, add_watch, set_alert, alerts, remove_alert, check_alerts, remove, list, quote, or portfolio.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException ex)
        {
            return new ToolResult(false, "Indian market data request timed out: " + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or InvalidOperationException)
        {
            return new ToolResult(false, "Indian market monitor failed safely: " + ex.Message);
        }
    }

    private async Task<ToolResult> SetHoldingAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "symbol", "quantity", "average_buy_price", "exchange"))
            return new ToolResult(false, "set_holding accepts action, symbol, quantity, average_buy_price and optional exchange.");
        if (!TrySymbol(args, out var symbol, out var exchange, out var error)) return new ToolResult(false, error);
        if (!args.TryGetValue("quantity", out var quantityText) ||
            !long.TryParse(quantityText, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) ||
            quantity < 1 || quantity > 1_000_000_000)
            return new ToolResult(false, "quantity must be a whole number from 1 to 1,000,000,000.");
        if (!args.TryGetValue("average_buy_price", out var priceText) ||
            !decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var averagePrice) ||
            averagePrice <= 0 || averagePrice > 1_000_000_000m)
            return new ToolResult(false, "average_buy_price must be a positive rupee amount using a dot decimal separator.");

        await _storeGate.WaitAsync(token);
        try
        {
            var store = await LoadStoreAsync(token);
            var existing = store.Holdings.FirstOrDefault(x => x.Symbol == symbol && x.Exchange == exchange);
            if (existing is null)
            {
                if (store.Holdings.Count + store.Watchlist.Count >= MaxTrackedSymbols)
                    return new ToolResult(false, $"The local watchlist is limited to {MaxTrackedSymbols} total symbols.");
                store.Holdings.Add(new Holding { Symbol = symbol, Exchange = exchange, Quantity = quantity, AverageBuyPrice = averagePrice });
            }
            else
            {
                existing.Quantity = quantity;
                existing.AverageBuyPrice = averagePrice;
            }
            await SaveStoreAsync(store, token);
            return new ToolResult(true, $"Saved {symbol} ({exchange}): {quantity:N0} shares at average buy price {Money(averagePrice)}. This is stored locally on this device.");
        }
        finally { _storeGate.Release(); }
    }

    private async Task<ToolResult> AddWatchAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "symbol", "exchange"))
            return new ToolResult(false, "add_watch accepts action, symbol and optional exchange.");
        if (!TrySymbol(args, out var symbol, out var exchange, out var error)) return new ToolResult(false, error);
        await _storeGate.WaitAsync(token);
        try
        {
            var store = await LoadStoreAsync(token);
            if (store.Watchlist.Any(x => x.Symbol == symbol && x.Exchange == exchange))
                return new ToolResult(true, $"{symbol} ({exchange}) is already on your watchlist.");
            if (store.Holdings.Count + store.Watchlist.Count >= MaxTrackedSymbols)
                return new ToolResult(false, $"The local watchlist is limited to {MaxTrackedSymbols} total symbols.");
            store.Watchlist.Add(new WatchItem { Symbol = symbol, Exchange = exchange });
            await SaveStoreAsync(store, token);
            return new ToolResult(true, $"Added {symbol} ({exchange}) to your watchlist.");
        }
        finally { _storeGate.Release(); }
    }

    private async Task<ToolResult> RemoveAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "symbol", "exchange"))
            return new ToolResult(false, "remove accepts action, symbol and optional exchange.");
        var defaultExchange = args.ContainsKey("exchange") ? "NSE" : "ANY";
        if (!TrySymbol(args, out var symbol, out var exchange, out var error, defaultExchange)) return new ToolResult(false, error);
        await _storeGate.WaitAsync(token);
        try
        {
            var store = await LoadStoreAsync(token);
            var holdingsRemoved = store.Holdings.RemoveAll(x => x.Symbol == symbol && (exchange == "ANY" || x.Exchange == exchange));
            var watchRemoved = store.Watchlist.RemoveAll(x => x.Symbol == symbol && (exchange == "ANY" || x.Exchange == exchange));
            if (holdingsRemoved + watchRemoved == 0) return new ToolResult(false, $"No saved holding or watch item found for {symbol}.");
            await SaveStoreAsync(store, token);
            return new ToolResult(true, $"Removed {symbol} from {holdingsRemoved} holding record(s) and {watchRemoved} watchlist record(s).");
        }
        finally { _storeGate.Release(); }
    }

    private async Task<ToolResult> ListAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action")) return new ToolResult(false, "list accepts only action.");
        var store = await LoadStoreAsync(token);
        var output = new StringBuilder("Your private Indian equity list (stored locally):");
        output.AppendLine().AppendLine("Holdings:");
        if (store.Holdings.Count == 0) output.AppendLine("  (none)");
        foreach (var item in store.Holdings.OrderBy(x => x.Symbol))
            output.AppendLine($"  {item.Symbol} ({item.Exchange}) | {item.Quantity:N0} shares | avg cost {Money(item.AverageBuyPrice)}");
        output.AppendLine("Watchlist:");
        if (store.Watchlist.Count == 0) output.AppendLine("  (none)");
        foreach (var item in store.Watchlist.OrderBy(x => x.Symbol))
            output.AppendLine($"  {item.Symbol} ({item.Exchange})");
        output.AppendLine("EOD price alerts:");
        if (store.Alerts.Count == 0) output.AppendLine("  (none)");
        foreach (var alert in store.Alerts.OrderBy(x => x.Symbol))
            output.AppendLine($"  {alert.Id} | {alert.Symbol} ({alert.Exchange}) | close {alert.Condition} {Money(alert.ThresholdPrice)} | {(alert.Enabled ? "ACTIVE" : $"TRIGGERED on {alert.TriggeredDate:yyyy-MM-dd} at {Money(alert.TriggeredClose ?? 0m)}")}");
        output.AppendLine("This local TejHQ list is separate from broker holdings. Use dhan_readonly to inspect a configured Dhan account.");
        return new ToolResult(true, output.ToString().Trim());
    }

    private async Task<ToolResult> QuoteAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "symbol", "exchange"))
            return new ToolResult(false, "quote accepts action, symbol and optional exchange.");
        if (!TrySymbol(args, out var symbol, out var exchange, out var error)) return new ToolResult(false, error);
        var result = await FetchQuoteAsync(symbol, exchange, token);
        return result.Quote is null
            ? new ToolResult(false, $"{symbol} ({exchange}): {result.Error}")
            : new ToolResult(true, FormatQuote(symbol, exchange, result.Quote));
    }

    private async Task<ToolResult> PortfolioAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action")) return new ToolResult(false, "portfolio accepts only action.");
        var store = await LoadStoreAsync(token);
        var tracked = store.Holdings.Select(x => new TrackedSymbol(x.Symbol, x.Exchange, x.Quantity, x.AverageBuyPrice))
            .Concat(store.Watchlist.Select(x => new TrackedSymbol(x.Symbol, x.Exchange, null, null)))
            .ToArray();
        if (tracked.Length == 0)
            return new ToolResult(true, "Your local holdings and watchlist are empty. Add a holding with set_holding (symbol, quantity, average_buy_price) or a symbol with add_watch.");

        using var gate = new SemaphoreSlim(4, 4);
        var results = await Task.WhenAll(tracked.Select(async item =>
        {
            await gate.WaitAsync(token);
            try
            {
                var result = await FetchQuoteAsync(item.Symbol, item.Exchange, token);
                return new PortfolioQuote(item, result.Quote, result.Error);
            }
            finally { gate.Release(); }
        }));

        var output = new StringBuilder("INDIAN EQUITY PORTFOLIO — latest available daily close");
        output.AppendLine().AppendLine("Source: TejHQ EOD OHLCV from NSE/BSE public bhavcopy. This is not a live/intraday feed.");
        output.AppendLine();
        decimal invested = 0, marketValue = 0;
        var pricedHoldings = 0;
        foreach (var row in results)
        {
            var item = row.Item;
            if (row.Quote is null)
            {
                output.AppendLine($"{item.Symbol} ({item.Exchange}): unavailable — {row.Error}");
                continue;
            }
            output.AppendLine(FormatQuote(item.Symbol, item.Exchange, row.Quote));
            if (item.Quantity is not null && item.AverageBuyPrice is not null)
            {
                var cost = item.Quantity.Value * item.AverageBuyPrice.Value;
                var value = item.Quantity.Value * row.Quote.Close;
                var pnl = value - cost;
                invested += cost;
                marketValue += value;
                pricedHoldings++;
                output.AppendLine($"  Position: {item.Quantity.Value:N0} shares | cost {Money(cost)} | value {Money(value)} | unrealized P&L {SignedMoney(pnl)} ({SignedPct(cost == 0 ? 0 : pnl / cost * 100m)})");
            }
        }

        if (pricedHoldings > 0)
        {
            var pnl = marketValue - invested;
            output.AppendLine().AppendLine($"Priced holdings: {pricedHoldings}/{store.Holdings.Count}");
            output.AppendLine($"Invested value: {Money(invested)}");
            output.AppendLine($"Market value:   {Money(marketValue)}");
            output.AppendLine($"Unrealized P&L: {SignedMoney(pnl)} ({SignedPct(invested == 0 ? 0 : pnl / invested * 100m)})");
        }
        else if (store.Holdings.Count > 0)
        {
            output.AppendLine().AppendLine("No holdings could be priced; portfolio totals are omitted rather than guessed.");
        }
        output.AppendLine().AppendLine("Important: average buy prices must reflect your current share units after any split/bonus. Verify prices and corporate actions with your broker before acting.");
        return new ToolResult(true, output.ToString().Trim());
    }


    public async Task<bool> HasMonitorItemsAsync(CancellationToken cancellationToken = default)
    {
        await _storeGate.WaitAsync(cancellationToken);
        try
        {
            var store = await LoadStoreAsync(cancellationToken);
            return store.Holdings.Count > 0 || store.Watchlist.Count > 0 || store.Alerts.Any(alert => alert.Enabled);
        }
        finally { _storeGate.Release(); }
    }

    private async Task<ToolResult> SetAlertAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "symbol", "condition", "price", "exchange"))
            return new ToolResult(false, "set_alert accepts action, symbol, condition=above|below, price and optional exchange.");
        if (!TrySymbol(args, out var symbol, out var exchange, out var error)) return new ToolResult(false, error);
        if (exchange == "ANY") return new ToolResult(false, "set_alert requires one exchange: NSE or BSE.");
        var condition = args.TryGetValue("condition", out var conditionText) ? conditionText.Trim().ToLowerInvariant() : "";
        if (condition is not ("above" or "below")) return new ToolResult(false, "condition must be above or below.");
        if (!args.TryGetValue("price", out var priceText) ||
            !decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var threshold) ||
            threshold <= 0 || threshold > 1_000_000_000m)
            return new ToolResult(false, "price must be a positive rupee amount using a dot decimal separator.");

        await _storeGate.WaitAsync(token);
        try
        {
            var store = await LoadStoreAsync(token);
            var existing = store.Alerts.FirstOrDefault(alert =>
                alert.Symbol == symbol && alert.Exchange == exchange &&
                alert.Condition == condition && alert.ThresholdPrice == threshold);
            if (existing is not null)
            {
                existing.Enabled = true;
                existing.TriggeredDate = null;
                existing.TriggeredClose = null;
                existing.TriggeredAtUtc = null;
                await SaveStoreAsync(store, token);
                return new ToolResult(true, $"Re-armed EOD alert {existing.Id}: {symbol} ({exchange}) close {condition} ₹{threshold:N2}.");
            }
            if (store.Alerts.Count >= MaxPriceAlerts)
                return new ToolResult(false, $"You can save up to {MaxPriceAlerts} price alerts.");
            var alert = new PriceAlert
            {
                Id = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
                Symbol = symbol,
                Exchange = exchange,
                Condition = condition,
                ThresholdPrice = threshold
            };
            store.Alerts.Add(alert);
            await SaveStoreAsync(store, token);
            return new ToolResult(true, $"Saved EOD price alert {alert.Id}: {symbol} ({exchange}) close {condition} ₹{threshold:N2}. It triggers only on a fresh daily close, not intraday ticks.");
        }
        finally { _storeGate.Release(); }
    }

    private async Task<ToolResult> AlertsAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action")) return new ToolResult(false, "alerts accepts only action.");
        var store = await LoadStoreAsync(token);
        if (store.Alerts.Count == 0) return new ToolResult(true, "No saved Indian-equity price alerts.");
        var output = new StringBuilder("Saved EOD price alerts (local):");
        foreach (var alert in store.Alerts.OrderBy(x => x.Symbol))
            output.AppendLine().AppendLine($"{alert.Id} | {alert.Symbol} ({alert.Exchange}) | close {alert.Condition} ₹{alert.ThresholdPrice:N2} | {(alert.Enabled ? "ACTIVE" : $"TRIGGERED {alert.TriggeredDate:yyyy-MM-dd} at ₹{alert.TriggeredClose:N2}")}");
        return new ToolResult(true, output.ToString().Trim());
    }

    private async Task<ToolResult> RemoveAlertAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action", "alert_id") || !args.TryGetValue("alert_id", out var id) || string.IsNullOrWhiteSpace(id))
            return new ToolResult(false, "remove_alert requires alert_id from the alerts list.");
        await _storeGate.WaitAsync(token);
        try
        {
            var store = await LoadStoreAsync(token);
            var removed = store.Alerts.RemoveAll(alert => string.Equals(alert.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return new ToolResult(false, $"No price alert found with ID {id}.");
            await SaveStoreAsync(store, token);
            return new ToolResult(true, $"Removed price alert {id}.");
        }
        finally { _storeGate.Release(); }
    }

    private async Task<ToolResult> CheckAlertsAsync(Dictionary<string, string> args, CancellationToken token)
    {
        if (!HasOnly(args, "action")) return new ToolResult(false, "check_alerts accepts only action.");
        var store = await LoadStoreAsync(token);
        var active = store.Alerts.Where(alert => alert.Enabled).Select(alert => new PriceAlert
        {
            Id = alert.Id, Symbol = alert.Symbol, Exchange = alert.Exchange, Condition = alert.Condition,
            ThresholdPrice = alert.ThresholdPrice, Enabled = true
        }).ToArray();
        if (active.Length == 0) return new ToolResult(true, "No active EOD price alerts.");

        using var gate = new SemaphoreSlim(4, 4);
        var checks = await Task.WhenAll(active.Select(async alert =>
        {
            await gate.WaitAsync(token);
            try
            {
                var result = await FetchQuoteAsync(alert.Symbol, alert.Exchange, token);
                return new AlertCheck(alert, result.Quote, result.Error);
            }
            finally { gate.Release(); }
        }));

        var triggeredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new StringBuilder("EOD PRICE-ALERT CHECK — daily close only, not live/intraday");
        foreach (var check in checks)
        {
            if (check.Quote is null)
            {
                output.AppendLine().AppendLine($"{check.Alert.Id} {check.Alert.Symbol}: unavailable — {check.Error}");
                continue;
            }
            var age = DateOnly.FromDateTime(DateTime.Now).DayNumber - check.Quote.Date.DayNumber;
            if (age > 4)
            {
                output.AppendLine().AppendLine($"{check.Alert.Id} {check.Alert.Symbol}: stale EOD data dated {check.Quote.Date:yyyy-MM-dd}; alert not evaluated.");
                continue;
            }
            var crossed = check.Alert.Condition == "above"
                ? check.Quote.Close >= check.Alert.ThresholdPrice
                : check.Quote.Close <= check.Alert.ThresholdPrice;
            if (crossed)
            {
                triggeredIds.Add(check.Alert.Id);
                output.AppendLine().AppendLine($"TRIGGERED {check.Alert.Id}: {check.Alert.Symbol} ({check.Alert.Exchange}) latest close ₹{check.Quote.Close:N2} on {check.Quote.Date:yyyy-MM-dd} is {check.Alert.Condition} ₹{check.Alert.ThresholdPrice:N2}.");
            }
            else
            {
                output.AppendLine().AppendLine($"{check.Alert.Id}: {check.Alert.Symbol} latest close ₹{check.Quote.Close:N2} on {check.Quote.Date:yyyy-MM-dd}; threshold ₹{check.Alert.ThresholdPrice:N2} not crossed.");
            }
        }

        if (triggeredIds.Count > 0)
        {
            await _storeGate.WaitAsync(token);
            try
            {
                var latestStore = await LoadStoreAsync(token);
                foreach (var alert in latestStore.Alerts.Where(alert => triggeredIds.Contains(alert.Id) && alert.Enabled))
                {
                    var check = checks.First(item => item.Alert.Id == alert.Id);
                    alert.Enabled = false;
                    alert.TriggeredDate = check.Quote!.Date;
                    alert.TriggeredClose = check.Quote.Close;
                    alert.TriggeredAtUtc = DateTimeOffset.UtcNow;
                }
                await SaveStoreAsync(latestStore, token);
            }
            finally { _storeGate.Release(); }
        }
        return new ToolResult(true, output.ToString().Trim());
    }

    private async Task<(MarketQuote? Quote, string? Error)> FetchQuoteAsync(string symbol, string exchange, CancellationToken token)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-14);
        var url = $"{ApiBase}/v1/ohlcv/{exchange.ToLowerInvariant()}/{Uri.EscapeDataString(symbol)}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        try
        {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("JARVIS-Council/1.0 (personal EOD portfolio monitor)");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            return (null, $"market-data service returned HTTP {(int)response.StatusCode}; retry later.");
        var payload = await response.Content.ReadAsStringAsync(token);
        if (payload.Length > MaxResponseCharacters)
            return (null, "market-data response exceeded the 1 MB safety limit.");
        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return (null, "the EOD service returned an unexpected response shape.");
        var bars = new List<MarketBar>();
        foreach (var row in data.EnumerateArray())
        {
            var dateText = ReadString(row, "date");
            if (!DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            var close = ReadDecimal(row, "close");
            if (close is null || close <= 0) continue;
            bars.Add(new MarketBar(date, close.Value, ReadDecimal(row, "prev_close"), ReadLong(row, "volume")));
        }
        var latest = bars.OrderByDescending(x => x.Date).FirstOrDefault();
        if (latest is null) return (null, "no recent EOD rows were returned for this symbol.");
        var previousClose = latest.PreviousClose ?? bars.Where(x => x.Date < latest.Date).OrderByDescending(x => x.Date).Select(x => (decimal?)x.Close).FirstOrDefault();
        return (new MarketQuote(latest.Date, latest.Close, previousClose, latest.Volume), null);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (null, ex is TaskCanceledException ? "market-data request timed out." : "market-data request failed: " + ex.Message);
        }
    }

    private static string FormatQuote(string symbol, string exchange, MarketQuote quote)
    {
        var daily = quote.PreviousClose is > 0 ? (quote.Close - quote.PreviousClose.Value) / quote.PreviousClose.Value * 100m : (decimal?)null;
        var age = DateOnly.FromDateTime(DateTime.Now).DayNumber - quote.Date.DayNumber;
        var freshness = age > 4 ? " | STALE DATA — check broker" : "";
        return $"{symbol} ({exchange}) | EOD close {Money(quote.Close)} | date {quote.Date:yyyy-MM-dd}" +
               (daily is null ? "" : $" | day {SignedPct(daily.Value)}") +
               (quote.Volume is null ? "" : $" | volume {quote.Volume.Value:N0}") + freshness;
    }

    private async Task<WatchlistDocument> LoadStoreAsync(CancellationToken token)
    {
        if (!File.Exists(_watchlistPath)) return new WatchlistDocument();
        var json = await File.ReadAllTextAsync(_watchlistPath, token);
        return JsonSerializer.Deserialize<WatchlistDocument>(json, JsonOptions) ?? new WatchlistDocument();
    }

    private async Task SaveStoreAsync(WatchlistDocument store, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(_watchlistPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new IOException("Could not resolve the local watchlist directory.");
        Directory.CreateDirectory(directory);
        var temporary = _watchlistPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(store, JsonOptions), new UTF8Encoding(false), token);
            File.Move(temporary, _watchlistPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool TrySymbol(Dictionary<string, string> args, out string symbol, out string exchange, out string error, string defaultExchange = "NSE")
    {
        symbol = args.TryGetValue("symbol", out var raw) ? raw.Trim().ToUpperInvariant() : "";
        exchange = args.TryGetValue("exchange", out var exchangeRaw) ? exchangeRaw.Trim().ToUpperInvariant() : defaultExchange;
        error = "";
        if (!SymbolPattern.IsMatch(symbol)) { error = "symbol must be a valid NSE/BSE trading symbol (1–30 letters, digits, hyphen or ampersand)."; return false; }
        if (exchange is not ("NSE" or "BSE" or "ANY")) { error = "exchange must be NSE or BSE."; return false; }
        return true;
    }

    private static bool HasOnly(Dictionary<string, string> args, params string[] allowed)
    {
        var set = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        return args.Keys.All(set.Contains);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number)) return number;
        return null;
    }

    private static long? ReadLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return null;
    }

    private static string Money(decimal value) => "₹" + value.ToString("N2", CultureInfo.GetCultureInfo("en-IN"));
    private static string SignedMoney(decimal value) => (value >= 0 ? "+" : "-") + Money(Math.Abs(value));
    private static string SignedPct(decimal value) => (value >= 0 ? "+" : "") + value.ToString("0.00", CultureInfo.InvariantCulture) + "%";

    private sealed class WatchlistDocument
    {
        [JsonPropertyName("holdings")] public List<Holding> Holdings { get; set; } = [];
        [JsonPropertyName("watchlist")] public List<WatchItem> Watchlist { get; set; } = [];
        [JsonPropertyName("alerts")] public List<PriceAlert> Alerts { get; set; } = [];
    }
    private sealed class Holding
    {
        public string Symbol { get; set; } = "";
        public string Exchange { get; set; } = "NSE";
        public long Quantity { get; set; }
        public decimal AverageBuyPrice { get; set; }
    }
    private sealed class WatchItem
    {
        public string Symbol { get; set; } = "";
        public string Exchange { get; set; } = "NSE";
    }
    private sealed class PriceAlert
    {
        public string Id { get; set; } = "";
        public string Symbol { get; set; } = "";
        public string Exchange { get; set; } = "NSE";
        public string Condition { get; set; } = "above";
        public decimal ThresholdPrice { get; set; }
        public bool Enabled { get; set; } = true;
        public DateOnly? TriggeredDate { get; set; }
        public decimal? TriggeredClose { get; set; }
        public DateTimeOffset? TriggeredAtUtc { get; set; }
    }
    private sealed record AlertCheck(PriceAlert Alert, MarketQuote? Quote, string? Error);
    private sealed record TrackedSymbol(string Symbol, string Exchange, long? Quantity, decimal? AverageBuyPrice);
    private sealed record PortfolioQuote(TrackedSymbol Item, MarketQuote? Quote, string? Error);
    private sealed record MarketBar(DateOnly Date, decimal Close, decimal? PreviousClose, long? Volume);
    private sealed record MarketQuote(DateOnly Date, decimal Close, decimal? PreviousClose, long? Volume);
}
