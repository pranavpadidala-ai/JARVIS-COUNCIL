using System.Globalization;
using System.Net;
using System.Text;
using Council.Execution.Tools;

namespace Council.App.Finance;

/// <summary>
/// Resolves NSE/BSE cash-equity trading symbols against Dhan's official instrument master.
/// This public reference-data lookup does not require broker credentials and cannot trade.
/// </summary>
public sealed class DhanInstrumentLookupTool : ITool
{
    private const string MasterUrl = "https://images.dhan.co/api-data/api-scrip-master.csv";
    private const int MaxCsvCharacters = 40_000_000;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(6);
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _cachedCsv;
    private DateTimeOffset _cachedAt;

    public DhanInstrumentLookupTool(HttpClient? httpClient = null) => _http = httpClient ?? SharedClient;

    public string Name => "dhan_instrument_lookup";
    public string Description =>
        "Resolve an exact Indian cash-equity trading symbol to its current Dhan security ID and exchange segment using Dhan's official instrument master. Required: symbol. Optional: exchange=NSE|BSE (defaults NSE). Example RELIANCE. Returns at most five exact matches. No credentials or trading permissions required.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        if (!arguments.TryGetValue("symbol", out var symbol) || string.IsNullOrWhiteSpace(symbol))
            return new ToolResult(false, "dhan_instrument_lookup requires a non-empty symbol.");
        if (arguments.Keys.Any(key => !key.Equals("symbol", StringComparison.OrdinalIgnoreCase) &&
                                      !key.Equals("exchange", StringComparison.OrdinalIgnoreCase)))
            return new ToolResult(false, "Unexpected argument. Supported arguments are symbol and exchange.");

        symbol = symbol.Trim().ToUpperInvariant();
        if (symbol.Length > 40 || symbol.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '&' or ' ')))
            return new ToolResult(false, "Symbol contains unsupported characters or is too long.");

        var exchange = arguments.TryGetValue("exchange", out var rawExchange)
            ? rawExchange.Trim().ToUpperInvariant()
            : "NSE";
        if (exchange is not ("NSE" or "BSE"))
            return new ToolResult(false, "exchange must be NSE or BSE.");

        try
        {
            var csv = await GetMasterAsync(cancellationToken);
            var rows = ParseCsv(csv);
            if (rows.Count < 2)
                return new ToolResult(false, "Dhan instrument master was empty or incomplete.");

            var headers = rows[0].Select((name, index) => (name: name.Trim(), index))
                .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
            string[] required = ["SEM_SMST_SECURITY_ID", "SEM_EXM_EXCH_ID", "SEM_SEGMENT", "SEM_INSTRUMENT_NAME", "SEM_TRADING_SYMBOL", "SEM_CUSTOM_SYMBOL", "SM_SYMBOL_NAME"];
            if (required.Any(name => !headers.ContainsKey(name)))
                return new ToolResult(false, "Dhan instrument master format changed; required columns were not found.");

            string Field(List<string> row, string name) =>
                headers.TryGetValue(name, out var index) && index < row.Count ? row[index].Trim() : "";

            var matches = new List<(string Id, string Segment, string Trading, string Display, string Instrument, string Series)>();
            foreach (var row in rows.Skip(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Count < required.Length) continue;
                if (!Field(row, "SEM_EXM_EXCH_ID").Equals(exchange, StringComparison.OrdinalIgnoreCase) ||
                    !Field(row, "SEM_SEGMENT").Equals("E", StringComparison.OrdinalIgnoreCase))
                    continue;

                var trading = Field(row, "SEM_TRADING_SYMBOL");
                var symbolName = Field(row, "SM_SYMBOL_NAME");
                var custom = Field(row, "SEM_CUSTOM_SYMBOL");
                if (!trading.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
                    !symbolName.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
                    !custom.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                    continue;

                var id = Field(row, "SEM_SMST_SECURITY_ID");
                if (id.Length == 0 || id.Any(ch => !char.IsAsciiDigit(ch))) continue;
                var segment = exchange + "_EQ";
                var instrument = Field(row, "SEM_INSTRUMENT_NAME");
                var series = headers.ContainsKey("SEM_SERIES") ? Field(row, "SEM_SERIES") : "";
                if (!matches.Any(item => item.Id == id && item.Segment == segment))
                    matches.Add((id, segment, trading, custom, instrument, series));
                if (matches.Count == 5) break;
            }

            if (matches.Count == 0)
                return new ToolResult(false, $"No exact {exchange} cash-equity match found for '{symbol}'. Check the exchange or symbol spelling; derivatives are not included in this lookup.");

            var lines = new List<string>
            {
                $"DHAN INSTRUMENT LOOKUP — {exchange} cash equities",
                "Matched against Dhan's official instrument master. Verify the selected company before requesting a quote."
            };
            foreach (var match in matches)
                lines.Add($"Symbol: {match.Trading} | Security ID: {match.Id} | Exchange segment: {match.Segment}" +
                          (match.Display.Length > 0 ? $" | Display: {match.Display}" : "") +
                          (match.Series.Length > 0 ? $" | Series: {match.Series}" : ""));
            lines.Add("For a Dhan LTP snapshot, pass the exact returned security_id and exchange_segment to dhan_readonly action=quote.");
            return new ToolResult(true, string.Join(Environment.NewLine, lines));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return new ToolResult(false, "Dhan instrument-master download timed out."); }
        catch (HttpRequestException) { return new ToolResult(false, "Could not download Dhan's instrument master. Check the internet connection and retry."); }
        catch (InvalidDataException ex) { return new ToolResult(false, ex.Message); }
        catch (DecoderFallbackException) { return new ToolResult(false, "Dhan instrument master was not valid UTF-8 text."); }
    }

    private async Task<string> GetMasterAsync(CancellationToken cancellationToken)
    {
        if (_cachedCsv is not null && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
            return _cachedCsv;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedCsv is not null && DateTimeOffset.UtcNow - _cachedAt < CacheLifetime)
                return _cachedCsv;

            using var response = await _http.GetAsync(MasterUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException($"Dhan instrument master returned HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength is > MaxCsvCharacters)
                throw new InvalidDataException("Dhan instrument master exceeded the 40 MB safety limit.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
            var csv = await reader.ReadToEndAsync(cancellationToken);
            if (csv.Length == 0 || csv.Length > MaxCsvCharacters)
                throw new InvalidDataException("Dhan instrument master was empty or exceeded the 40 MB safety limit.");
            _cachedCsv = csv;
            _cachedAt = DateTimeOffset.UtcNow;
            return csv;
        }
        finally { _refreshLock.Release(); }
    }

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"' && field.Length == 0) quoted = true;
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                if (row.Any(value => value.Length > 0)) rows.Add(row);
                row = [];
            }
            else field.Append(ch);
        }

        if (quoted) throw new InvalidDataException("Dhan instrument master contains an unterminated quoted CSV field.");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Any(value => value.Length > 0)) rows.Add(row);
        }
        return rows;
    }
}
