using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Queries.GetFuelStations;
using Domain.Models.Fuel;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelPriceCalendar(
  ICarrierFuelPrices fuelPrices,
  DateOnly today,
  List<FuelStationDto> current
)
{
  private readonly Dictionary<DateOnly, List<FuelStationDto>> days = new()
  {
    [today] = current,
  };
  private readonly Dictionary<
    DateOnly,
    Dictionary<Guid, PricedFuelStation>
  > pricedDays = new();
  private (bool UseIfta, double? CadToUsd)? pricing;
  public List<DateOnly> Dates => days.Keys.Order().ToList();
  public string Signature => UsFuelDiscountSignature.Calendar(days);

  public async Task<List<FuelCandidate>> PriceAsync(
    IReadOnlyList<FuelCandidate> candidates,
    IReadOnlyDictionary<string, DateTimeOffset> arrivals,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    var key = (profile.UseIfta, profile.CadToUsd);
    if (pricing != key)
    {
      pricedDays.Clear();
      pricing = key;
    }
    var fallback = Prices(today, current, profile);
    var result = new List<FuelCandidate>(candidates.Count);
    foreach (var candidate in candidates)
    {
      ct.ThrowIfCancellationRequested();
      DateTimeOffset? arrival = arrivals.TryGetValue(
        candidate.VisitKey,
        out var at
      )
        ? at
        : null;
      var date = arrival.HasValue
        ? DateOnly.FromDateTime(arrival.Value.DateTime)
        : today;
      if (!days.TryGetValue(date, out var stations))
      {
        var response =
          await fuelPrices.ReadAsync(date, ct)
          ?? throw new RoutePlanningException(
            "Fuel prices are temporarily unavailable."
          );
        days[date] = stations = response;
      }
      var prices = Prices(date, stations, profile);
      var quote = prices.GetValueOrDefault(candidate.Station.StationId);
      var estimated = !arrival.HasValue || quote is null;
      quote ??= fallback.GetValueOrDefault(candidate.Station.StationId);
      if (quote is null)
        continue;
      var stop = new FuelPlanStop
      {
        StationId = quote.Station.StationId,
        Name = quote.Station.Name,
        Address = quote.Station.Address,
        Country = quote.Station.Country,
        Point = quote.Station.Point,
        YourPrice = quote.Station.YourPrice,
        EconomicPrice = quote.Station.EconomicPrice,
        Currency = quote.Station.Currency,
        Unit = quote.Station.Unit,
        PriceDate = quote.Station.PriceDate,
        DetourMinutes = candidate.Station.DetourMinutes,
        EstimatedArrival = arrival,
        PriceEstimated = estimated,
      };
      result.Add(
        candidate with
        {
          Station = stop,
          PriceUsd = quote.CashUsd,
          EconomicPriceUsd = quote.EconomicUsd,
        }
      );
    }
    return result;
  }

  private Dictionary<Guid, PricedFuelStation> Prices(
    DateOnly date,
    List<FuelStationDto> stations,
    TruckRouteProfile profile
  )
  {
    if (!pricedDays.TryGetValue(date, out var prices))
      pricedDays[date] = prices = FuelRegionGrid
        .Prices(stations, profile, date)
        .ToDictionary(x => x.Station.StationId);
    return prices;
  }
}
