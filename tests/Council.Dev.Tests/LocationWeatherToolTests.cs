using System.Net;
using System.Text;
using Council.App.Devices;
using Xunit;

namespace Council.Dev.Tests;

public sealed class LocationWeatherToolTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "jarvis-home-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_home)) File.Delete(_home);
    }

    private sealed class FakeWeb : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Urls.Add(url);
            var json = url.Contains("geocoding-api")
                ? "{\"results\":[{\"latitude\":17.385,\"longitude\":78.4867}]}"
                : url.Contains("api.open-meteo.com")
                    ? "{\"current\":{\"temperature_2m\":24.5,\"apparent_temperature\":25,\"relative_humidity_2m\":60,\"precipitation\":0,\"weather_code\":2,\"wind_speed_10m\":8}," +
                      "\"daily\":{\"time\":[\"2026-11-22\"],\"weather_code\":[61],\"temperature_2m_max\":[29],\"temperature_2m_min\":[18],\"precipitation_probability_max\":[40]}}"
                    : url.Contains("reverse")
                        ? "{\"display_name\":\"Gachibowli, Hyderabad, Telangana\"}"
                        : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task ReportsPlaceAndWeatherWithRoundedCoordinates()
    {
        var web = new FakeWeb();
        var tool = new LocationWeatherTool(_ => Task.FromResult<GeoFix?>(new GeoFix(17.440123456, 78.348765432, 40, "Windows location")), new HttpClient(web), _home);

        var where = await tool.ExecuteAsync(new() { ["action"] = "where" });
        Assert.Contains("Gachibowli", where.Message);
        Assert.Contains("lat=17.44&lon=78.349", web.Urls.Single(u => u.Contains("reverse")));

        var weather = await tool.ExecuteAsync(new() { ["action"] = "weather" });
        Assert.Contains("partly cloudy, 24.5°C", weather.Message);
        Assert.Contains("rain chance 40%", weather.Message);
    }

    [Fact]
    public async Task FallsBackToTheSavedHomeCity()
    {
        var tool = new LocationWeatherTool(_ => Task.FromResult<GeoFix?>(null), new HttpClient(new FakeWeb()), _home);
        var none = await tool.ExecuteAsync(new() { ["action"] = "where" });
        Assert.False(none.Success);
        Assert.Contains("Privacy & security > Location", none.Message);

        Assert.True((await tool.ExecuteAsync(new() { ["action"] = "set_home", ["place"] = "Hyderabad" })).Success);
        var where = await tool.ExecuteAsync(new() { ["action"] = "where" });
        Assert.True(where.Success);
        Assert.Contains("saved home city", where.Message);
    }

    [Fact]
    public void MeasuresDistance() => Assert.InRange(LocationWeatherTool.DistanceKm(17.385, 78.4867, 17.44, 78.3488), 15, 17);
}
