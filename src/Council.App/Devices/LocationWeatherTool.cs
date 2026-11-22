using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Council.Execution.Tools;

namespace Council.App.Devices;

public sealed record GeoFix(double Lat, double Lon, int? AccuracyMeters, string Source);

/// <summary>
/// "Where am I", weather and nearby places. Location comes from the Windows location service; when that is
/// off or has no fix, a home city the user saved is used instead. Place names come from OpenStreetMap
/// (Nominatim) and weather from Open-Meteo, both free with no account. Coordinates are rounded to about
/// 100 m before they leave the laptop.
/// </summary>
public sealed class LocationWeatherTool : ITool
{
    private readonly Func<CancellationToken, Task<GeoFix?>> _locate;
    private readonly HttpClient _http;
    private readonly string _homePath;

    public LocationWeatherTool(Func<CancellationToken, Task<GeoFix?>>? locate = null, HttpClient? http = null, string? homePath = null)
    {
        _locate = locate ?? WindowsLocationAsync;
        _http = http ?? CreateClient();
        _homePath = homePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JARVIS-Council", "home-city.json");
    }

    public string Name => "location";

    public string Description =>
        "Location and weather. Arguments: action = where | weather | nearby | set_home. " +
        "where: current place. weather: current weather and the next 3 days (optional place to look up another city). " +
        "nearby: query (e.g. hospital, petrol pump, hardware shop) for places close by with links to the map. " +
        "set_home: place (city name) used when Windows location is off. Location needs Windows Settings > Privacy & security > Location turned on.";

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        string Arg(string k) => args.TryGetValue(k, out var v) ? v.Trim() : "";
        try
        {
            switch (Arg("action").ToLowerInvariant())
            {
                case "set_home":
                    var home = await GeocodeAsync(Arg("place"), cancellationToken);
                    if (home is null) return new ToolResult(false, $"Could not find '{Arg("place")}'.");
                    Directory.CreateDirectory(Path.GetDirectoryName(_homePath)!);
                    await File.WriteAllTextAsync(_homePath, JsonSerializer.Serialize(new { Arg = Arg("place"), home.Lat, home.Lon }), cancellationToken);
                    return new ToolResult(true, $"Home city set to {Arg("place")}.");
                case "where":
                {
                    var fix = await CurrentAsync(cancellationToken);
                    if (fix is null) return NoLocation();
                    var name = await ReverseAsync(fix, cancellationToken);
                    var accuracy = fix.AccuracyMeters is { } a ? $" (accurate to about {a} m)" : "";
                    return new ToolResult(true, $"{name ?? $"{fix.Lat:0.###}, {fix.Lon:0.###}"}{accuracy}. Source: {fix.Source}. " +
                                                $"Map: https://www.openstreetmap.org/?mlat={fix.Lat:0.####}&mlon={fix.Lon:0.####}#map=15/{fix.Lat:0.####}/{fix.Lon:0.####}");
                }
                case "weather":
                {
                    var fix = Arg("place").Length > 0 ? await GeocodeAsync(Arg("place"), cancellationToken) : await CurrentAsync(cancellationToken);
                    if (fix is null) return Arg("place").Length > 0 ? new ToolResult(false, $"Could not find '{Arg("place")}'.") : NoLocation();
                    return new ToolResult(true, await WeatherAsync(fix, cancellationToken));
                }
                case "nearby":
                {
                    var query = Arg("query");
                    if (query.Length is 0 or > 80) return new ToolResult(false, "query is required, e.g. hospital.");
                    var fix = await CurrentAsync(cancellationToken);
                    if (fix is null) return NoLocation();
                    return new ToolResult(true, await NearbyAsync(fix, query, cancellationToken));
                }
                default:
                    return new ToolResult(false, "action must be where, weather, nearby or set_home.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or KeyNotFoundException or InvalidOperationException)
        {
            return new ToolResult(false, "Location service failed: " + ex.Message);
        }
    }

    private static ToolResult NoLocation() => new(false,
        "I can't get the location: Windows location is off or has no fix yet. Turn on Settings > Privacy & security > Location " +
        "(and 'Let desktop apps access your location'), or tell me your home city so I can use it.");

    private async Task<GeoFix?> CurrentAsync(CancellationToken token)
    {
        var fix = await _locate(token);
        if (fix is not null) return fix;
        if (!File.Exists(_homePath)) return null;
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(_homePath, token));
        return new GeoFix(doc.RootElement.GetProperty("Lat").GetDouble(), doc.RootElement.GetProperty("Lon").GetDouble(), null, "saved home city");
    }

    private async Task<GeoFix?> GeocodeAsync(string place, CancellationToken token)
    {
        if (place.Length is 0 or > 100) return null;
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(
            $"https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&name={Uri.EscapeDataString(place)}", token));
        if (!doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0) return null;
        var r = results[0];
        return new GeoFix(r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), null, "city lookup");
    }

    private async Task<string?> ReverseAsync(GeoFix fix, CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(
            $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=16&lat={Round(fix.Lat)}&lon={Round(fix.Lon)}", token));
        return doc.RootElement.TryGetProperty("display_name", out var name) ? name.GetString() : null;
    }

    private async Task<string> WeatherAsync(GeoFix fix, CancellationToken token)
    {
        var url = $"https://api.open-meteo.com/v1/forecast?latitude={Round(fix.Lat)}&longitude={Round(fix.Lon)}" +
                  "&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m" +
                  "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&forecast_days=3&timezone=auto";
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, token));
        var c = doc.RootElement.GetProperty("current");
        var lines = new List<string>
        {
            $"Now: {Describe(c.GetProperty("weather_code").GetInt32())}, {c.GetProperty("temperature_2m").GetDouble():0.#}°C " +
            $"(feels {c.GetProperty("apparent_temperature").GetDouble():0.#}°C), humidity {c.GetProperty("relative_humidity_2m").GetDouble():0}%, " +
            $"wind {c.GetProperty("wind_speed_10m").GetDouble():0} km/h."
        };
        var d = doc.RootElement.GetProperty("daily");
        for (var i = 0; i < d.GetProperty("time").GetArrayLength(); i++)
        {
            var day = DateOnly.Parse(d.GetProperty("time")[i].GetString()!, CultureInfo.InvariantCulture);
            var rain = d.GetProperty("precipitation_probability_max")[i];
            lines.Add($"{day:ddd dd MMM}: {Describe(d.GetProperty("weather_code")[i].GetInt32())}, " +
                      $"{d.GetProperty("temperature_2m_min")[i].GetDouble():0}-{d.GetProperty("temperature_2m_max")[i].GetDouble():0}°C" +
                      (rain.ValueKind == JsonValueKind.Number ? $", rain chance {rain.GetDouble():0}%" : ""));
        }
        return string.Join("\n", lines) + "\n(Open-Meteo)";
    }

    private async Task<string> NearbyAsync(GeoFix fix, string query, CancellationToken token)
    {
        const double box = 0.05; // about 5 km each way
        var url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=8&bounded=1&q={Uri.EscapeDataString(query)}" +
                  $"&viewbox={Round(fix.Lon - box)},{Round(fix.Lat + box)},{Round(fix.Lon + box)},{Round(fix.Lat - box)}";
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(url, token));
        var places = doc.RootElement.EnumerateArray().Select(p =>
        {
            var lat = double.Parse(p.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
            var lon = double.Parse(p.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
            var km = DistanceKm(fix.Lat, fix.Lon, lat, lon);
            return (km, text: $"- {p.GetProperty("display_name").GetString()} ({km:0.0} km) https://www.openstreetmap.org/?mlat={lat:0.#####}&mlon={lon:0.#####}");
        }).OrderBy(p => p.km).Select(p => p.text).ToList();
        return places.Count == 0 ? $"No '{query}' found within about 5 km on OpenStreetMap." : string.Join("\n", places);
    }

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        double R(double d) => d * Math.PI / 180;
        var a = Math.Pow(Math.Sin(R(lat2 - lat1) / 2), 2) + Math.Cos(R(lat1)) * Math.Cos(R(lat2)) * Math.Pow(Math.Sin(R(lon2 - lon1) / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(a));
    }

    public static string Describe(int code) => code switch
    {
        0 => "clear sky", 1 or 2 => "partly cloudy", 3 => "overcast", 45 or 48 => "fog",
        >= 51 and <= 57 => "drizzle", >= 61 and <= 67 => "rain", >= 71 and <= 77 => "snow",
        >= 80 and <= 82 => "rain showers", >= 95 => "thunderstorm", _ => "mixed weather"
    };

    private static string Round(double value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("JARVIS-desktop-assistant/1.0"); // Nominatim asks for an identifying agent
        return http;
    }

    private static async Task<GeoFix?> WindowsLocationAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) return null;
        string? script = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && script is null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "Get-DeviceLocation.ps1");
            if (File.Exists(candidate)) script = candidate;
        }
        if (script is null) return null;
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script }) start.ArgumentList.Add(a);
        using var process = Process.Start(start);
        if (process is null) return null;
        var output = await process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var line = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'));
        if (line is null) return null;
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (!root.GetProperty("ok").GetBoolean()) return null;
        return new GeoFix(root.GetProperty("lat").GetDouble(), root.GetProperty("lon").GetDouble(),
            root.TryGetProperty("accuracy_m", out var acc) ? acc.GetInt32() : null, "Windows location");
    }
}
