using Application.Diagnostics;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

// Which chain of fuel stops wins.
//
// Each candidate chain is priced for the day the truck would arrive, given
// to the optimizer, and replayed against the schedule. A chain is judged on
// the schedule first - one that makes an appointment late, or a driver run
// out of hours, loses to one that does not, whatever it saves - and on
// money only between chains the schedule cannot tell apart. An extra stop
// has to earn its place: it must save at least twenty dollars against a
// feasible chain with fewer.
//
// Every chain looked at is recorded with why it lost, because "why not the
// cheaper station" is the first thing a dispatcher asks.
public static class FuelChainComparison
{
  public sealed record Result(
    FuelPlan? Fuel,
    List<FuelCandidate> Purchases,
    List<FuelRouteCheck> Checks,
    FuelRouteCheck? Winner,
    int Evaluated
  );

  public static async Task<Result> RunAsync(
    List<List<FuelCandidate>> chains,
    int comparisonLimit,
    FuelPriceCalendar calendar,
    FuelScheduleContext schedule,
    TruckRoute baseline,
    FuelSearchGeometry geometry,
    double startAccessMiles,
    FuelOptimizationMemo optimization,
    TruckRouteProfile p,
    CancellationToken ct
  )
  {
    var initialMinutes = FuelAccessEstimate.DrivingMinutes(startAccessMiles);
    FuelPlan? bestFuel = null;
    List<FuelCandidate> bestPurchases = [];
    (int Cycle, int Unknown, int Late) bestImpact = (
      int.MaxValue,
      int.MaxValue,
      int.MaxValue
    );
    double bestScore = double.PositiveInfinity;
    var evaluatedRoutes = 0;
    var seen = new HashSet<string>();
    var checks = new List<FuelRouteCheck>();
    FuelRouteCheck? winner = null;
    foreach (var chain in chains)
    {
      if (evaluatedRoutes >= comparisonLimit)
        break;
      var ordered = chain.OrderBy(x => x.AlongMiles).ToList();
      ordered = await calendar.PriceAsync(
        ordered,
        schedule.Arrivals(
          baseline,
          ordered,
          geometry,
          startAccessMiles,
          true,
          ct
        ),
        p,
        ct
      );
      if (!seen.Add(string.Join(",", ordered.Select(x => x.VisitKey))))
        continue;
      var check = new FuelRouteCheck
      {
        Stations = ordered.Select(x => x.Station.Name).ToList(),
      };
      ct.ThrowIfCancellationRequested();
      evaluatedRoutes++;
      checks.Add(check);
      var extraMiles =
        startAccessMiles + ordered.Sum(x => x.ExtraInMiles + x.ExtraOutMiles);
      var extraMinutes =
        initialMinutes + ordered.Sum(x => x.Station.DetourMinutes);
      check.ExtraMiles = extraMiles;
      check.ExtraMinutes = extraMinutes;
      FuelPlan fuel;
      List<FuelCandidate> purchases;
      try
      {
        using (PerformanceStages.Start("fuel", "optimizer"))
          (fuel, purchases) = optimization.Take(ordered);
      }
      catch (RoutePlanningException)
      {
        check.Result = "Cannot preserve fuel reserve";
        continue;
      }
      // Do not keep an unnecessary waypoint when the optimizer buys nothing
      // there.
      if (fuel.Stops.Count != ordered.Count)
      {
        check.Result = "Includes a station without a useful purchase";
        continue;
      }
      fuel.EconomicCostUsd += initialMinutes / 60 * p.DriverHourlyCostUsd;
      if (
        FuelScheduleRanking.CanSkipReplay(
          fuel,
          0,
          p.DriverHourlyCostUsd,
          bestFuel is null ? null : bestImpact,
          bestScore,
          bestFuel?.Stops.Count ?? 0
        )
      )
      {
        check.Result = "Higher estimated cost before schedule replay";
        continue;
      }
      fuel.ScheduleImpact =
        chain.Count == 0 && startAccessMiles == 0
          ? schedule.Baseline
          : schedule.Evaluate(
            FuelAccessEstimate.TimingRoute(
              baseline,
              purchases,
              startAccessMiles
            ),
            ct
          );
      var impact = FuelScheduleRanking.For(fuel.ScheduleImpact);
      // Access driving time is already priced by the optimizer; add only
      // further schedule delay.
      var timeCost = Math.Max(
        0,
        FuelScheduleRanking.DelayCost(
          fuel.ScheduleImpact,
          extraMiles,
          extraMinutes,
          p.DriverHourlyCostUsd
        )
          - extraMinutes / 60 * p.DriverHourlyCostUsd
      );
      var score =
        fuel.EconomicCostUsd + fuel.ExpectedFutureFuelCostUsd + timeCost;
      check.CostUsd = score;
      check.Result = "Higher total cost";
      if (impact.CompareTo(bestImpact) > 0)
      {
        check.Result = "Worse schedule feasibility";
        continue;
      }
      if (
        impact.CompareTo(bestImpact) == 0
        && FuelStopEconomy.Compare(
          score,
          fuel.Stops.Count,
          bestScore,
          bestFuel!.Stops.Count
        ) >= 0
      )
      {
        if (score < bestScore)
          check.Result = "Additional stops save less than $20 each";
        continue;
      }
      fuel.ExtraMinutes = extraMinutes;
      fuel.EconomicCostUsd += timeCost;
      fuel.SavingsUsd = null;
      winner = check;
      bestScore = score;
      bestFuel = fuel;
      bestPurchases = purchases;
      bestImpact = impact;
    }
    return new(bestFuel, bestPurchases, checks, winner, evaluatedRoutes);
  }
}
