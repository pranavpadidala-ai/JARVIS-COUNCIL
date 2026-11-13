using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Finance;

/// <summary>
/// Read-only DhanHQ v2 integration. Credentials are read from the process environment
/// and are never persisted, logged, or included in tool output.
/// </summary>
public sealed class DhanReadOnlyTool : ITool
{
    private const string ApiBase = "https://api.dhan.co/v2/";
    private const int MaxResponseBytes = 1_000_000;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;
    private readonly Func<string?> _clientId;
    private readonly Func<string?> _accessToken;

    public DhanReadOnlyTool(HttpClient? httpClient = null, Func<string?>? clientId = null, Func<string?>? accessToken = null)
    {
        _http = httpClient ?? SharedClient;
        _clientId = clientId ?? DhanCredentialStore.ReadClientId;
        _accessToken = accessToken ?? DhanCredentialStore.ReadAccessToken;
    }

    public string Name => "dhan_readonly";

    public string Description =>
        "Read-only DhanHQ account and market-data connector. Actions: profile, holdings, positions, quote. " +
        "quote requires exchange_segment (for example NSE_EQ or BSE_EQ) and security_id (Dhan numeric security ID). " +
        "Reads Dhan credentials from Windows Credential Manager (preferred) or DHAN_CLIENT_ID / DHAN_ACCESS_TOKEN environment variables. Read-only; cannot place, modify, cancel, or convert orders/positions.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("action", out var action) || string.IsNullOrWhiteSpace(action))
            return new ToolResult(false, "dhan_readonly requires action=profile|holdings|positions|quote.");

        var normalized = action.Trim().ToLowerInvariant();
        var allowed = normalized switch
        {
            "profile" or "holdings" or "positions" => new[] { "action" },
            "quote" => new[] { "action", "exchange_segment", "security_id" },
            _ => Array.Empty<string>()
        };
        if (allowed.Length == 0)
            return new ToolResult(false, "Unsupported Dhan action. Available actions are profile, holdings, positions, and quote.");
        if (arguments.Keys.Any(key => !allowed.Contains(key, StringComparer.OrdinalIgnoreCase)))
            return new ToolResult(false, "Unexpected argument supplied. This connector accepts only the documented read-only arguments.");

        var clientId = _clientId();
        var token = _accessToken();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(token))
            return new ToolResult(false, "Dhan is not connected. Store the credentials with scripts/Set-DhanCredentials.ps1 or set DHAN_CLIENT_ID and DHAN_ACCESS_TOKEN locally, then restart JARVIS. Do not paste credentials into chat.");
        clientId = clientId.Trim();
        token = token.Trim();
        if (clientId.Length > 24 || clientId.Length == 0 || clientId.Any(ch => !char.IsAsciiDigit(ch)))
            return new ToolResult(false, "DHAN_CLIENT_ID must be the numeric client ID issued by Dhan.");
        if (token.Length > 8192 || token.Any(char.IsControl))
            return new ToolResult(false, "DHAN_ACCESS_TOKEN has an invalid format. Refresh it in Dhan Web and set it locally.");

        try
        {
            using var request = BuildRequest(normalized, arguments, clientId.Trim(), token.Trim());
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Authentication failed or the access token has expired. Generate a fresh token in Dhan Web.",
                    HttpStatusCode.TooManyRequests => "Dhan rate limit reached. Wait before retrying.",
                    _ => $"Dhan returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})."
                };
                return new ToolResult(false, detail);
            }

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
                return new ToolResult(false, "Dhan response exceeded the 1 MB safety limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memory = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                if (memory.Length + read > MaxResponseBytes)
                    return new ToolResult(false, "Dhan response exceeded the 1 MB safety limit.");
                await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            using var document = JsonDocument.Parse(memory.ToArray());
            return new ToolResult(true, FormatResponse(normalized, document.RootElement));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new ToolResult(false, "Dhan request timed out.");
        }
        catch (ArgumentException ex)
        {
            return new ToolResult(false, ex.Message);
        }
        catch (OverflowException)
        {
            return new ToolResult(false, "security_id must be a valid 32-bit Dhan security ID.");
        }
        catch (HttpRequestException)
        {
            return new ToolResult(false, "Could not reach DhanHQ. Check the internet connection and try again.");
        }
        catch (JsonException)
        {
            return new ToolResult(false, "Dhan returned an invalid or unsupported JSON response.");
        }
    }

    private static HttpRequestMessage BuildRequest(string action, Dictionary<string, string> arguments, string clientId, string token)
    {
        var request = action switch
        {
            "profile" => new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(ApiBase), "profile")),
            "holdings" => new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(ApiBase), "holdings")),
            "positions" => new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(ApiBase), "positions")),
            "quote" => BuildQuoteRequest(arguments),
            _ => throw new InvalidOperationException("Unsupported read-only action.")
        };
        request.Headers.TryAddWithoutValidation("access-token", token);
        request.Headers.TryAddWithoutValidation("client-id", clientId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static HttpRequestMessage BuildQuoteRequest(Dictionary<string, string> arguments)
    {
        var segment = arguments.TryGetValue("exchange_segment", out var rawSegment) ? rawSegment.Trim().ToUpperInvariant() : "";
        var securityId = arguments.TryGetValue("security_id", out var rawId) ? rawId.Trim() : "";
        var validSegments = new HashSet<string>(StringComparer.Ordinal)
        {
            "NSE_EQ", "BSE_EQ", "NSE_FNO", "BSE_FNO", "NSE_CURRENCY", "BSE_CURRENCY", "MCX_COMM", "IDX_I"
        };
        if (!validSegments.Contains(segment))
            throw new ArgumentException("exchange_segment must be a supported Dhan segment such as NSE_EQ, BSE_EQ, NSE_FNO, or IDX_I.");
        if (securityId.Length is < 1 or > 10 || securityId.Any(ch => !char.IsAsciiDigit(ch)) ||
            !int.TryParse(securityId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedId))
            throw new ArgumentException("security_id must be a valid positive 32-bit Dhan security ID.");

        var payload = JsonSerializer.Serialize(new Dictionary<string, int[]>
        {
            [segment] = [parsedId]
        });
        return new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(ApiBase), "marketfeed/ltp"))
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }

    private static string FormatResponse(string action, JsonElement root)
    {
        if (action == "profile")
        {
            var client = GetText(root, "dhanClientId");
            var validity = GetText(root, "tokenValidity");
            var dataPlan = GetText(root, "dataPlan");
            var dataValidity = GetText(root, "dataValidity");
            return "Dhan connection verified.\n" +
                   (client is null ? "" : $"Client ID: {client}\n") +
                   (validity is null ? "" : $"Token validity: {validity}\n") +
                   (dataPlan is null ? "" : $"Data API plan: {dataPlan}\n") +
                   (dataValidity is null ? "" : $"Data validity: {dataValidity}\n") +
                   "Read-only access confirmed; no trading action was performed.";
        }

        if (action == "holdings")
        {
            if (root.ValueKind != JsonValueKind.Array) return "Dhan holdings response was not an array.";
            var lines = new List<string> { "DHAN DEMAT HOLDINGS (broker-reported)" };
            decimal invested = 0;
            var count = 0;
            foreach (var row in root.EnumerateArray())
            {
                var symbol = GetText(row, "tradingSymbol") ?? "Unknown";
                var exchange = GetText(row, "exchange") ?? "Unknown";
                var isin = GetText(row, "isin") ?? "";
                var quantity = GetLong(row, "totalQty") ?? 0;
                var avgCost = GetDecimal(row, "avgCostPrice");
                lines.Add($"{symbol} ({exchange}) | Qty {quantity:N0}" +
                          (avgCost is null ? "" : $" | Average cost ₹{avgCost.Value:N2}") +
                          (isin.Length == 0 ? "" : $" | ISIN {isin}"));
                if (avgCost is not null && quantity > 0) { invested += avgCost.Value * quantity; count++; }
            }
            lines.Add(count == 0 ? "No priced holdings returned." : $"Approximate cost value of {count} priced holding(s): ₹{invested:N2}");
            lines.Add("Source: Dhan account API. Values are broker-reported; verify corporate actions and settlement quantities.");
            return string.Join("\n", lines);
        }

        if (action == "positions")
        {
            if (root.ValueKind != JsonValueKind.Array) return "Dhan positions response was not an array.";
            var lines = new List<string> { "DHAN OPEN POSITIONS (broker-reported)" };
            foreach (var row in root.EnumerateArray())
            {
                var symbol = GetText(row, "tradingSymbol") ?? "Unknown";
                var product = GetText(row, "positionType") ?? GetText(row, "productType") ?? "Unknown";
                var exchange = GetText(row, "exchangeSegment") ?? "Unknown";
                var quantity = GetLong(row, "netQty") ?? 0;
                var pnl = GetDecimal(row, "realizedProfit") + GetDecimal(row, "unrealizedProfit");
                lines.Add($"{symbol} | {exchange} | {product} | Net qty {quantity:N0}" +
                          (pnl is null ? "" : $" | Realized + unrealized P&L ₹{pnl.Value:N2}"));
            }
            if (root.GetArrayLength() == 0) lines.Add("No open positions returned.");
            return string.Join("\n", lines);
        }

        if (action == "quote")
        {
            var lines = new List<string> { "DHAN MARKET SNAPSHOT — real-time at request time" };
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var segment in data.EnumerateObject())
                foreach (var instrument in segment.Value.EnumerateObject())
                {
                    var ltp = GetDecimal(instrument.Value, "last_price");
                    lines.Add($"{segment.Name} security {instrument.Name}: " +
                              (ltp is null ? "LTP unavailable" : $"LTP ₹{ltp.Value:N2}"));
                }
            }
            if (lines.Count == 1) lines.Add("No quote data returned for the requested instrument.");
            lines.Add("Market data is a snapshot, not a guaranteed executable price.");
            return string.Join("\n", lines);
        }

        return "Unsupported Dhan response.";
    }

    private static string? GetText(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static long? GetLong(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;

    private static decimal? GetDecimal(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
}
