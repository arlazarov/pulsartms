using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Infrastructure.Integrations.Http;

namespace Infrastructure.Integrations.BankOfCanada;

public sealed class BankOfCanadaExchangeRateProvider(
  HttpClient client,
  TimeProvider clock
) : BaseApiService(client), IFuelExchangeRateProvider
{
  private const string Endpoint =
    "https://www.bankofcanada.ca/valet/observations/FXUSDCAD/json?recent=1";

  public async Task<FuelExchangeRate> ReadAsync(CancellationToken ct)
  {
    Response? response;
    try
    {
      response = await GetAsync<Response>(Endpoint, ct);
    }
    catch (JsonException)
    {
      throw InvalidResponse();
    }
    var now = clock.GetUtcNow().UtcDateTime;
    if (
      response?.Observations is not [{ } observation]
      || !DateOnly.TryParseExact(
        observation.Date,
        "yyyy-MM-dd",
        CultureInfo.InvariantCulture,
        DateTimeStyles.None,
        out var observedOn
      )
      || observedOn == default
      || observedOn > DateOnly.FromDateTime(now)
      || !decimal.TryParse(
        observation.Value?.Rate,
        NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture,
        out var cadPerUsd
      )
      || cadPerUsd is < .5m or > 10m
    )
      throw InvalidResponse();
    var usdPerCad = 1m / cadPerUsd;
    return new(usdPerCad, observedOn, now);
  }

  private static InvalidOperationException InvalidResponse() =>
    new("The official exchange-rate observation is invalid.");

  private sealed record Response(Observation[]? Observations);

  private sealed record Observation(
    [property: JsonPropertyName("d")] string? Date,
    [property: JsonPropertyName("FXUSDCAD")] Value? Value
  );

  private sealed record Value([property: JsonPropertyName("v")] string? Rate);
}
