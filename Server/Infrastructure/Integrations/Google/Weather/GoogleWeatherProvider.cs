using System.Globalization;
using Application.Features.Fleet.Interfaces;
using Domain.Models.Fleet;
using Infrastructure.Integrations.Http;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.Google.Weather;

public sealed class GoogleWeatherProvider(
  HttpClient client,
  IConfiguration configuration
) : BaseApiService(client), IWeatherProvider
{
  public async Task<WeatherReading?> GetCurrentAsync(
    decimal latitude,
    decimal longitude,
    CancellationToken ct
  )
  {
    var key = configuration["GoogleWeather:ApiKey"];
    if (string.IsNullOrWhiteSpace(key))
      return null;
    var coordinates = string.Create(
      CultureInfo.InvariantCulture,
      $"?location.latitude={latitude}&location.longitude={longitude}"
    );
    var url =
      "https://weather.googleapis.com/v1/currentConditions:lookup"
      + coordinates
      + "&unitsSystem=METRIC&languageCode=en";
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Add("X-Goog-Api-Key", key);
    var data = await SendAsync<Conditions>(request, ct);
    if (
      data?.Temperature is not { Degrees: { } degrees, Unit: "CELSIUS" }
      || degrees is < -100 or > 70
      || data.CurrentTime == default
    )
      return null;
    return new(
      degrees,
      data.WeatherCondition?.Type ?? "",
      data.WeatherCondition?.Description?.Text ?? "",
      data.IsDaytime,
      data.CurrentTime
    );
  }

  private sealed record Conditions(
    DateTimeOffset CurrentTime,
    bool IsDaytime,
    Temperature? Temperature,
    Condition? WeatherCondition
  );

  private sealed record Temperature(decimal? Degrees, string? Unit);

  private sealed record Condition(string? Type, Description? Description);

  private sealed record Description(string? Text);
}
