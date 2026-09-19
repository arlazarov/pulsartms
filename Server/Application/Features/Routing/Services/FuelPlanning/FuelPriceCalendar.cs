using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelPriceCalendar(
  ISender sender,
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
        var response = await sender.Send(new GetFuelStationsQuery(date), ct);
        if (!response.Success || response.Response is null)
          throw new RoutePlanningException(
            "Fuel prices are temporarily unavailable."
          );
        days[date] = stations = response.Response;
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
