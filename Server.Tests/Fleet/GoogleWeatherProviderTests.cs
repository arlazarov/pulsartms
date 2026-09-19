using Infrastructure.Integrations.Google.Weather;
using Microsoft.Extensions.Configuration;

namespace Server.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class GoogleWeatherProviderTests
{
  [Theory]
  [InlineData(0, "CELSIUS", true)]
  [InlineData(-40, "CELSIUS", true)]
  [InlineData(200, "CELSIUS", false)]
  [InlineData(32, "FAHRENHEIT", false)]
  public async Task ReadsValidatedMetricWeatherUsingHeaderCredentials(
    decimal degrees,
    string unit,
    bool accepted
  )
  {
    using var handler = new WeatherHttpHandler(
      new
      {
        currentTime = "2026-09-13T18:00:00Z",
        isDaytime = true,
        temperature = new { degrees, unit },
        weatherCondition = new
        {
          type = "RAIN",
          description = new { text = "Rain" },
        },
      }
    );
    using var client = new HttpClient(handler);
    var config = new ConfigurationBuilder()
      .AddInMemoryCollection(
        new Dictionary<string, string?>
        {
          ["GoogleWeather:ApiKey"] = "test-key",
        }
      )
      .Build();
    var result = await new GoogleWeatherProvider(
      client,
      config
    ).GetCurrentAsync(35.5m, -99m, default);
    Assert.Equal(accepted, result is not null);
    if (accepted)
    {
      Assert.Equal(degrees, result!.Celsius);
      Assert.Equal("RAIN", result.Condition);
    }
    Assert.Equal("test-key", handler.Key);
    Assert.DoesNotContain("test-key", handler.Uri!.ToString());
    Assert.Contains("location.latitude=35.5", handler.Uri.Query);
    Assert.Contains("unitsSystem=METRIC", handler.Uri.Query);
  }

  [Fact]
  public async Task UnconfiguredWeatherDoesNotCallGoogle()
  {
    using var handler = new WeatherHttpHandler(new { });
    using var client = new HttpClient(handler);
    var result = await new GoogleWeatherProvider(
      client,
      new ConfigurationBuilder().Build()
    ).GetCurrentAsync(35, -99, default);
    Assert.Null(result);
    Assert.Equal(0, handler.Calls);
  }
}
