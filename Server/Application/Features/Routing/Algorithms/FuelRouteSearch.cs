using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;

namespace Application.Features.Routing.Algorithms;

public static class FuelRouteSearch
{
  public static List<FuelCandidate> SelectCandidates(IReadOnlyList<FuelCandidate> candidates,
    double miles, double gallons, TruckRouteProfile profile, FuelArrivalPolicy arrival, FuelRegionOptions options,
    bool includeAccess = false, double initialAccessMiles = 0)
  {
    var range = (profile.TankGallons!.Value * profile.FillPercent / 100 - profile.ReserveGallons) * profile.Mpg!.Value;
    var binSize = Math.Max(50, Math.Min(range / 2, Math.Max(100, miles / 10)));
    var unique = candidates.GroupBy(x => x.VisitKey).Select(x => x.MinBy(c => c.EconomicPriceUsd)!).ToList();
    // Keep a complete projected path before spending the shortlist on alternatives.
    var coverage = ReachabilityBackbone(unique, miles, gallons, profile, arrival, options.CandidateShortlistLimit, includeAccess, initialAccessMiles);
    var reachable = (Math.Floor(gallons) - FuelReservePolicy.FirstArrivalMinimum(gallons, profile)) * profile.Mpg.Value - initialAccessMiles;
    var first = unique.Where(x => x.AlongMiles <= reachable).OrderBy(x => x.EconomicPriceUsd).Take(2);
    var lastAffordable = unique.Where(x => arrival.PoorArea && x.EconomicPriceUsd <= arrival.TopUpPriceCeilingUsd)
      .OrderByDescending(x => x.AlongMiles).Take(2);
    // Every section gets a candidate before taking more from a densely populated section.
    var bins = unique.GroupBy(x => (int)(x.AlongMiles / binSize)).OrderBy(x => x.Key)
      .Select(g => new[] { g.MinBy(EstimatedAccessMiles)! }.Concat(g.OrderBy(x => x.EconomicPriceUsd).Take(2))
        .DistinctBy(x => x.VisitKey).ToList()).ToList();
    var spread = Enumerable.Range(0, 3).SelectMany(i => bins.Where(g => g.Count > i).Select(g => g[i]));
    var frontier = unique.GroupBy(x => (x.LegIndex, Section: (int)(x.AlongMiles / binSize)))
      .SelectMany(g => PriceAccessFrontier(g.ToList())).OrderBy(x => x.EconomicPriceUsd).ThenBy(EstimatedAccessMiles);
    return coverage.Concat(first).Concat(lastAffordable).Concat(FuelZoneSearch.Representatives(unique, options)).Concat(spread).DistinctBy(x => x.VisitKey)
      .Concat(frontier).DistinctBy(x => x.VisitKey).Take(options.CandidateShortlistLimit).OrderBy(x => x.AlongMiles).ToList();
  }

  private static List<FuelCandidate> PriceAccessFrontier(List<FuelCandidate> candidates)
  {
    var frontier = new List<FuelCandidate>();
    var lowestPrice = double.PositiveInfinity;
    foreach (var candidate in candidates.OrderBy(EstimatedAccessMiles).ThenBy(x => x.EconomicPriceUsd)
      .ThenBy(x => x.AlongMiles))
    {
      if (candidate.EconomicPriceUsd >= lowestPrice) continue;
      frontier.Add(candidate);
      lowestPrice = candidate.EconomicPriceUsd;
    }
    // Dominance is local to one route section and visit leg, never the entire trip.
    return frontier;
  }

  private static List<FuelCandidate> ReachabilityBackbone(IReadOnlyList<FuelCandidate> candidates,
    double miles, double gallons, TruckRouteProfile profile, FuelArrivalPolicy arrival, int limit, bool includeAccess, double initialAccessMiles)
  {
    var cap = Math.Floor(profile.TankGallons!.Value * profile.FillPercent / 100);
    var reserve = Math.Ceiling(profile.ReserveGallons);
    var minimum = Math.Max(reserve, Math.Ceiling(arrival.MinimumGallons));
    var ordered = candidates.Where(x => x.AlongMiles >= 0 && x.AlongMiles < miles
      && x.PriceUsd > 0 && x.EconomicPriceUsd > 0).OrderBy(x => x.AlongMiles).ToList();
    List<FuelCandidate>? full = null;
    foreach (var access in new[] { .5, 2, 5, 10, 20, double.PositiveInfinity })
    {
      var corridor = ordered.Where(x => EstimatedAccessMiles(x) <= access).ToList();
      var chain = new List<FuelCandidate>();
      double position = 0, fuel = Math.Floor(gallons), accessOut = initialAccessMiles;
      var index = 0;
      while (fuel - Math.Ceiling((Math.Max(0, miles - position) + accessOut) / profile.Mpg!.Value - 1e-9) < minimum)
      {
        FuelCandidate? farthest = null;
        var threshold = chain.Count == 0 ? FuelReservePolicy.FirstArrivalMinimum(gallons, profile) : reserve;
        var reachable = position + (fuel - threshold) * profile.Mpg.Value - accessOut;
        while (index < corridor.Count && corridor[index].AlongMiles <= reachable + 1e-9)
        {
          var candidate = corridor[index++];
          if ((candidate.AlongMiles > position || chain.Count == 0 && candidate.AlongMiles == position)
            && candidate.AlongMiles + (includeAccess ? candidate.ExtraInMiles : 0) <= reachable + 1e-9)
            farthest = candidate;
        }
        if (farthest is null) { chain = []; break; }
        chain.Add(farthest);
        position = farthest.AlongMiles;
        accessOut = includeAccess ? farthest.ExtraOutMiles : 0;
        fuel = cap;
      }
      if (chain.Count == 0) continue;
      if (chain.Count <= limit) return chain;
      full = chain;
    }
    if (full is not null)
      throw new RoutePlanningException($"The assigned itinerary exceeds the {limit}-visit bounded search envelope. A complete checked fuel plan is unavailable within this bound; the saved fuel plan has been kept.");
    return [];
  }

  public static List<List<FuelCandidate>> Chains(IReadOnlyList<FuelCandidate> candidates,
    double miles, double gallons, TruckRouteProfile profile, FuelArrivalPolicy arrival, CancellationToken ct = default,
    bool includeAccess = false, double initialAccessMiles = 0)
  {
    var originals = candidates.GroupBy(x => x.VisitKey).ToDictionary(x => x.Key, x => x.MinBy(c => c.EconomicPriceUsd)!);
    var stations = originals.Values.ToList();
    var results = new Dictionary<string, (List<FuelCandidate> Chain, double Score)>();
    var evaluated = new Dictionary<(string Visits, bool FewestStops), List<FuelCandidate>>();
    List<FuelCandidate> Evaluate(IEnumerable<FuelCandidate> input, bool fewestStops = false)
    {
      ct.ThrowIfCancellationRequested();
      var ordered = input.DistinctBy(x => x.VisitKey).OrderBy(x => x.AlongMiles).ToList();
      // Equal-mile visit order is significant to existing ties; do not replace this key with an unordered set.
      var inputKey = (string.Join(",", ordered.Select(x => x.VisitKey)), fewestStops);
      if (evaluated.TryGetValue(inputKey, out var previous)) return previous;
      var chain = includeAccess ? ordered : ordered.Select(x => x with { ExtraInMiles = 0, ExtraOutMiles = 0 }).ToList();
      try
      {
        var result = FuelOptimizer.OptimizeWithVisits(miles, gallons, profile, chain, 0, profile.UseIfta,
          fewestStops: fewestStops, compare: false, arrivalPolicy: arrival, initialAccessMiles: initialAccessMiles);
        var purchased = result.Purchases.Select(x => originals[x.VisitKey]).ToList();
        var key = string.Join(",", purchased.Select(x => x.VisitKey));
        var score = result.Plan.EconomicCostUsd + result.Plan.ExpectedFutureFuelCostUsd;
        if (!results.TryGetValue(key, out var saved) || score < saved.Score) results[key] = (purchased, score);
        evaluated.Add(inputKey, purchased);
        return purchased;
      }
      catch (RoutePlanningException)
      {
        List<FuelCandidate> unavailable = [];
        evaluated.Add(inputKey, unavailable);
        return unavailable;
      }
    }
    // Restore dearer quotes only until the cheap subset can cover the complete itinerary.
    // This seeds a checked alternative, not a price-only winner or a forced purchase.
    foreach (var price in stations.Select(x => x.EconomicPriceUsd).Distinct().Order())
    {
      var tier = stations.Where(x => x.EconomicPriceUsd <= price).ToList();
      if (Evaluate(tier).Count > 0 || results.ContainsKey("")) break;
    }
    var seed = Evaluate(stations);
    Evaluate(stations, true);
    Evaluate([]);
    // Cheap distant stations can dominate every economic seed. Keep feasible corridor
    // alternatives, including journeys requiring more than two fuel purchases.
    var previousCount = 0;
    foreach (var width in new[] { .5, 2, 5, 10, 20 })
    {
      var corridor = stations.Where(x => EstimatedAccessMiles(x) <= width).ToList();
      if (corridor.Count == previousCount || corridor.Count == stations.Count) continue;
      previousCount = corridor.Count;
      Evaluate(corridor);
      Evaluate(corridor, true);
    }
    if (includeAccess)
    {
      // Keep intermediate stop counts, not only the cheapest and fewest-stop extremes.
      // Re-optimize quantities and reserves across the complete horizon after each omission.
      for (var i = 0; i < seed.Count; i++) Evaluate(seed.Where((_, index) => index != i));
      return results.Values.OrderBy(x => (x.Score, x.Chain.Count), FuelStopEconomy.Comparer)
        .Select(x => x.Chain).ToList();
    }
    foreach (var station in stations)
    {
      Evaluate([station]);
      // Test an extra top-up before an expensive area, or replace one seed stop
      // without throwing away the rest of a long, otherwise feasible journey.
      Evaluate(seed.Append(station));
      for (var i = 0; i < seed.Count; i++) Evaluate(seed.Where((_, index) => index != i).Append(station));
    }
    for (var i = 0; i < stations.Count; i++)
      for (var j = i + 1; j < stations.Count; j++) Evaluate([stations[i], stations[j]]);
    // A fallback pair can require an earlier cheap fill even when the original seed
    // did not. Expand several competing seeds, not only the first projected optimum.
    var finalists = results.Values.OrderBy(x => x.Score).Take(4).Select(x => x.Chain)
      .Concat(results.Values.Where(x => x.Chain.Count > 0)
        .OrderBy(x => x.Chain.Sum(EstimatedAccessMiles)).ThenBy(x => x.Score).Take(2).Select(x => x.Chain))
      .DistinctBy(x => string.Join(",", x.Select(c => c.VisitKey))).ToList();
    foreach (var finalist in finalists)
      foreach (var station in stations) Evaluate(finalist.Append(station));
    return results.Values.OrderBy(x => x.Score).ThenBy(x => x.Chain.Count).Select(x => x.Chain).ToList();
  }

  internal static double EstimatedAccessMiles(FuelCandidate candidate) =>
    Math.Max(0, candidate.ExtraInMiles) + Math.Max(0, candidate.ExtraOutMiles);
}
