using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public static class FuelScheduleRanking
{
  public static (int Cycle, int Unknown, int Late) For(
    FuelScheduleImpact impact
  )
  {
    var introducedShortage = impact.Stops.Any(stop =>
      stop.CycleKnown && stop.CycleShort && !stop.BaselineCycleShort
    );
    var complete =
      impact.Complete
      && impact.CycleKnown
      && impact.Stops.Count > 0
      && impact.Stops.All(stop => stop.AddedLateMinutes.HasValue);
    // Protect newly missed appointments. Minute differences at already-late
    // stops
    // must not outweigh every possible fuel saving; delay has a monetary cost.
    var lateness = impact
      .Stops.Where(stop => stop.BaselineLateMinutes == 0)
      .Select(stop => stop.CandidateLateMinutes ?? 0)
      .DefaultIfEmpty(0)
      .Max();
    return (
      introducedShortage ? 1 : 0,
      complete ? 0 : 1,
      Math.Max(0, lateness)
    );
  }

  public static double DelayCost(
    FuelScheduleImpact impact,
    double extraMiles,
    double extraMinutes,
    double hourlyCost
  )
  {
    var roadMinutes = Math.Max(0, extraMinutes);
    var scheduledDelay = Math.Max(
      impact.AddedMinutes ?? 0,
      impact
        .Stops.Select(stop => stop.AddedLateMinutes ?? 0)
        .DefaultIfEmpty(0)
        .Max()
    );
    // Charge additional rest/wait once, without counting road time twice.
    return (roadMinutes + Math.Max(0, scheduledDelay - roadMinutes))
      / 60
      * hourlyCost;
  }

  public static bool CanSkipReplay(
    FuelPlan candidate,
    double extraMinutes,
    double hourlyCost,
    (int Cycle, int Unknown, int Late)? incumbentRank,
    double incumbentScore,
    int incumbentPurchaseCount
  )
  {
    if (
      incumbentRank is not { } rank
      || rank != (0, 0, 0)
      || !double.IsFinite(incumbentScore)
      || incumbentScore < 0
      || incumbentPurchaseCount < 0
      || candidate?.Stops is null
      || !double.IsFinite(candidate.EconomicCostUsd)
      || candidate.EconomicCostUsd < 0
      || !double.IsFinite(candidate.ExpectedFutureFuelCostUsd)
      || candidate.ExpectedFutureFuelCostUsd < 0
      || !double.IsFinite(extraMinutes)
      || !double.IsFinite(hourlyCost)
      || hourlyCost < 0
    )
      return false;
    // Replay cannot improve the ideal rank or charge less than the checked
    // extra road time.
    var lowerBound =
      candidate.EconomicCostUsd
      + candidate.ExpectedFutureFuelCostUsd
      + Math.Max(0, extraMinutes) / 60 * hourlyCost;
    return double.IsFinite(lowerBound)
      && FuelStopEconomy.Compare(
        lowerBound,
        candidate.Stops.Count,
        incumbentScore,
        incumbentPurchaseCount
      ) >= 0;
  }
}
