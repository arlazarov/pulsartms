using Application.Features.Routing.Models;
using Application.Features.Routing.Options;

namespace Application.Features.Routing.Algorithms;

public static class FuelRouteOccurrences
{
  public static List<FuelCandidate> Create(
    TruckRoute route,
    IReadOnlyList<PricedFuelStation> prices,
    FuelRegionOptions options,
    CancellationToken ct = default,
    FuelSearchGeometry? geometry = null,
    double? maximumAwayMiles = null,
    ISet<Guid>? corridorStations = null
  )
  {
    ct.ThrowIfCancellationRequested();
    var priceOrder = prices.Select(x => x.EconomicUsd).Order().ToArray();
    var reference =
      priceOrder.Length == 0 ? 0 : priceOrder[(priceOrder.Length - 1) / 4];
    var grid = new FuelRegionGrid(prices, options, reference);
    geometry ??= new(route, ct);
    var result = new List<FuelCandidate>();
    foreach (
      var station in prices
        .GroupBy(x => x.Station.StationId)
        .Select(g => g.MinBy(x => x.EconomicUsd)!)
    )
    {
      ct.ThrowIfCancellationRequested();
      double offset = 0;
      double? previousAlong = null;
      bool? zoneEligible = null;
      for (var i = 0; i < route.Legs.Count; i++)
      {
        ct.ThrowIfCancellationRequested();
        var match = geometry.MatchLeg(
          i,
          station.Station.Point,
          ct,
          maximumAwayMiles ?? double.PositiveInfinity
        );
        if (match.Away <= 2)
          corridorStations?.Add(station.Station.StationId);
        var along = offset + match.Along;
        offset += route.Legs[i].Miles;
        if (maximumAwayMiles is { } maximum && match.Away > maximum)
          continue;
        if (along < 1 || along >= route.Miles - 1)
          continue;
        if (match.Away > options.CandidateSearchMiles)
        {
          if (match.Away > options.ZoneSearchMiles)
            continue;
          zoneEligible ??=
            grid.Cell(station.Station.Point).MedianPriceUsd
            <= reference + options.ExpensivePremiumUsdPerGallon;
          if (zoneEligible != true)
            continue;
        }
        // Adjacent legs can project to the same mandatory boundary; that is one
        // visit.
        if (previousAlong is { } previous && Math.Abs(along - previous) < 1e-6)
          continue;
        result.Add(
          new(
            station.Station,
            along,
            match.Away,
            0,
            station.CashUsd,
            station.EconomicUsd
          )
          {
            LegIndex = i,
          }
        );
        previousAlong = along;
      }
    }
    return result.OrderBy(x => x.AlongMiles).ThenBy(x => x.LegIndex).ToList();
  }
}
