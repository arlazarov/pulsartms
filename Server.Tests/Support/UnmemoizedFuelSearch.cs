using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;

namespace Server.Tests.Support;

// The same scheduling without memoization is a parity oracle; quantities use the production optimizer.
internal static class UnmemoizedFuelSearch
{
  public static List<List<FuelCandidate>> Chains(IReadOnlyList<FuelCandidate> candidates,
    double miles, double gallons, TruckRouteProfile profile, FuelArrivalPolicy arrival)
  {
    var originals = candidates.GroupBy(x => x.VisitKey).ToDictionary(x => x.Key, x => x.MinBy(c => c.EconomicPriceUsd)!);
    var stations = originals.Values.ToList();
    var results = new Dictionary<string, (List<FuelCandidate> Chain, double Score)>();
    List<FuelCandidate> Evaluate(IEnumerable<FuelCandidate> input, bool fewestStops = false)
    {
      var chain = input.DistinctBy(x => x.VisitKey).OrderBy(x => x.AlongMiles)
        .Select(x => x with { ExtraInMiles = 0, ExtraOutMiles = 0 }).ToList();
      try
      {
        var result = FuelOptimizer.OptimizeWithVisits(miles, gallons, profile, chain, 0, profile.UseIfta,
          fewestStops: fewestStops, compare: false, arrivalPolicy: arrival);
        var purchased = result.Purchases.Select(x => originals[x.VisitKey]).ToList();
        var key = string.Join(",", purchased.Select(x => x.VisitKey));
        var score = result.Plan.EconomicCostUsd + result.Plan.ExpectedFutureFuelCostUsd;
        if (!results.TryGetValue(key, out var saved) || score < saved.Score) results[key] = (purchased, score);
        return purchased;
      }
      catch (RoutePlanningException) { return []; }
    }
    foreach (var price in stations.Select(x => x.EconomicPriceUsd).Distinct().Order())
    {
      var tier = stations.Where(x => x.EconomicPriceUsd <= price).ToList();
      if (Evaluate(tier).Count > 0 || results.ContainsKey("")) break;
    }
    var seed = Evaluate(stations);
    Evaluate(stations, true);
    Evaluate([]);
    var previousCount = 0;
    foreach (var width in new[] { .5, 2, 5, 10, 20 })
    {
      var corridor = stations.Where(x => Access(x) <= width).ToList();
      if (corridor.Count == previousCount || corridor.Count == stations.Count) continue;
      previousCount = corridor.Count;
      Evaluate(corridor);
      Evaluate(corridor, true);
    }
    foreach (var station in stations)
    {
      Evaluate([station]);
      Evaluate(seed.Append(station));
      for (var i = 0; i < seed.Count; i++) Evaluate(seed.Where((_, index) => index != i).Append(station));
    }
    for (var i = 0; i < stations.Count; i++)
      for (var j = i + 1; j < stations.Count; j++) Evaluate([stations[i], stations[j]]);
    var finalists = results.Values.OrderBy(x => x.Score).Take(4).Select(x => x.Chain)
      .Concat(results.Values.Where(x => x.Chain.Count > 0)
        .OrderBy(x => x.Chain.Sum(Access)).ThenBy(x => x.Score).Take(2).Select(x => x.Chain))
      .DistinctBy(x => string.Join(",", x.Select(c => c.VisitKey))).ToList();
    foreach (var finalist in finalists)
      foreach (var station in stations) Evaluate(finalist.Append(station));
    return results.Values.OrderBy(x => x.Score).ThenBy(x => x.Chain.Count).Select(x => x.Chain).ToList();
  }

  private static double Access(FuelCandidate station) => Math.Max(0, station.ExtraInMiles) + Math.Max(0, station.ExtraOutMiles);
}
