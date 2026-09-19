using Application.Diagnostics;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

// A calculation owns this memo and its immutable candidate inputs; finalists
// take ownership of cached results.
public sealed class FuelOptimizationMemo(
  double miles,
  double gallons,
  TruckRouteProfile profile,
  FuelArrivalPolicy arrival,
  int version,
  double initialAccessMiles
)
{
  private readonly Dictionary<
    (string Visits, bool Fewest),
    (FuelPlan Plan, List<FuelCandidate> Purchases)
  > results = [];
  public int Calculations { get; private set; }
  public int Reuses { get; private set; }

  public (FuelPlan Plan, List<FuelCandidate> Purchases) Evaluate(
    IReadOnlyList<FuelCandidate> ordered,
    bool fewest = false
  )
  {
    var key = (Key(ordered), fewest);
    if (results.TryGetValue(key, out var result))
    {
      Reuses++;
      return result;
    }
    Calculations++;
    using var timing = PerformanceStages.Start("fuel", "optimizer");
    result = FuelOptimizer.OptimizeWithVisits(
      miles,
      gallons,
      profile,
      ordered,
      version,
      profile.UseIfta,
      fewestStops: fewest,
      compare: false,
      arrivalPolicy: arrival,
      initialAccessMiles: initialAccessMiles
    );
    if (results.Count < 256)
      results.Add(key, result);
    return result;
  }

  public (FuelPlan Plan, List<FuelCandidate> Purchases) Take(
    IReadOnlyList<FuelCandidate> ordered
  )
  {
    var key = (Key(ordered), false);
    if (results.Remove(key, out var result))
    {
      Reuses++;
      return result;
    }
    result = Evaluate(ordered);
    results.Remove(key);
    return result;
  }

  private static string Key(IReadOnlyList<FuelCandidate> ordered) =>
    string.Join(
      ";",
      ordered.Select(candidate =>
        FormattableString.Invariant(
          $"{candidate.VisitKey}:{candidate.AlongMiles:R}:{candidate.ExtraInMiles:R}:{candidate.ExtraOutMiles:R}:{candidate.EntryMiles:R}:{candidate.ExitMiles:R}:{candidate.PriceUsd:R}:{candidate.EconomicPriceUsd:R}:{candidate.Station.DetourMinutes:R}"
        )
      )
    );
}
