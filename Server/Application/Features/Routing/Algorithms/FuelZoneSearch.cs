using Application.Features.Routing.Models;
using Application.Features.Routing.Options;

namespace Application.Features.Routing.Algorithms;

// Geographic groups are a search index. Their centers are never routing waypoints.
public static class FuelZoneSearch
{
  public static List<FuelCandidate> Representatives(IReadOnlyList<FuelCandidate> candidates, FuelRegionOptions options)
  {
    if (candidates.Count == 0) return [];
    var grid = new FuelRegionGrid([], options, 0);
    var groups = candidates.DistinctBy(x => x.VisitKey).GroupBy(x => (grid.Key(x.Station.Point), x.LegIndex)).Select(g => new
    {
      Median = g.Select(x => x.EconomicPriceUsd).Order().ElementAt(g.Count() / 2),
      Station = g.OrderBy(x => x.EconomicPriceUsd).ThenBy(x => x.ExtraInMiles).First(),
      Mile = g.Min(x => x.AlongMiles)
    }).ToList();
    // Keep inexpensive groups in different longitudinal sections of the journey.
    var width = Math.Max(options.CellMiles, candidates.Max(x => x.AlongMiles) / options.ZoneRoadChecks);
    var spread = groups.GroupBy(x => (int)(x.Mile / width))
      .Select(g => g.OrderBy(x => x.Median).ThenBy(x => x.Station.EconomicPriceUsd).First())
      .OrderBy(x => x.Median).ThenBy(x => x.Station.ExtraInMiles).Select(x => x.Station);
    return spread.Concat(groups.OrderBy(x => x.Median).ThenBy(x => x.Station.EconomicPriceUsd).Select(x => x.Station))
      .DistinctBy(x => x.VisitKey).Take(options.ZoneRoadChecks).ToList();
  }

  public static List<List<FuelCandidate>> Schedule(List<List<FuelCandidate>> scenarios,
    IReadOnlyList<FuelCandidate> zones)
  {
    // Reserve checks for zones even when the projected optimizer suggests no purchase.
    // A real alternative road may be shorter than the projected return to the old road.
    List<List<FuelCandidate>> result = [[]];
    // Scenarios already cover the complete projected itinerary. Try its lowest price
    // threshold first; the following near-road check protects the economic comparison.
    var cheapestTier = scenarios.Where(x => x.Count > 0).OrderBy(x => x.Max(c => c.EconomicPriceUsd)).FirstOrDefault();
    if (cheapestTier != null) result.Add(cheapestTier);
    // Reserve one existing check for a feasible near-road chain before cheap detours
    // consume the budget. Proximity is not a substitute for the actual road limits.
    var nearest = scenarios.Where(x => x.Count > 0).MinBy(x => x.Sum(FuelRouteSearch.EstimatedAccessMiles));
    if (nearest != null) result.Add(nearest);
    // Scenarios are cost-ordered. Reserve cheap corridor alternatives before
    // distant economic seeds consume the fixed road-check budget.
    foreach (var access in new[] { .5, 2, 5, 10 })
    {
      var corridor = scenarios.FirstOrDefault(x => x.Count > 0
        && x.Sum(FuelRouteSearch.EstimatedAccessMiles) <= access);
      if (corridor != null) result.Add(corridor);
    }
    result.AddRange(scenarios.Take(2));
    foreach (var zone in zones)
    {
      var containing = scenarios.FirstOrDefault(chain => chain.Any(x => x.VisitKey == zone.VisitKey));
      var scaffold = containing ?? scenarios.FirstOrDefault() ?? [];
      result.Add(scaffold.Append(zone).DistinctBy(x => x.VisitKey).OrderBy(x => x.AlongMiles).ToList());
    }
    result.AddRange(scenarios.Skip(2));
    result.AddRange(zones.Select(x => new List<FuelCandidate> { x }));
    var unique = result.Select(chain => chain.DistinctBy(x => x.VisitKey).OrderBy(x => x.AlongMiles).ToList())
      .DistinctBy(x => string.Join(",", x.Select(c => c.VisitKey))).ToList();
    var anchors = new HashSet<Guid>();
    var preferred = new List<List<FuelCandidate>>();
    var deferred = new List<List<FuelCandidate>>();
    foreach (var chain in unique)
    {
      var anchor = chain.Where(x => FuelRouteSearch.EstimatedAccessMiles(x) > 5)
        .MaxBy(FuelRouteSearch.EstimatedAccessMiles);
      // Different visits to the same distant station must not consume every check.
      if (anchor is null || anchors.Add(anchor.Station.StationId)
        || nearest is not null && chain.Select(x => x.VisitKey).SequenceEqual(nearest.Select(x => x.VisitKey))) preferred.Add(chain);
      else deferred.Add(chain);
    }
    return preferred.Concat(deferred).ToList();
  }
}
