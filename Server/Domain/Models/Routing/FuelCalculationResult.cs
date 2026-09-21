namespace Domain.Models.Routing;

public enum FuelCalculationStatus
{
  Feasible,
  NoFeasiblePlan,
  UnreachableStation,
  FeasibleBelowReserve,
}

public sealed record FuelCalculationResult(
  FuelCalculationStatus Status,
  FuelPlan? Plan,
  FuelRecommendations? Access
)
{
  public static FuelCalculationResult Feasible(FuelPlan plan, double reserve) =>
    new(
      plan.Stops.FirstOrDefault() is { } first && first.ArrivalGallons < reserve
        ? FuelCalculationStatus.FeasibleBelowReserve
        : FuelCalculationStatus.Feasible,
      plan,
      null
    );

  public static FuelCalculationResult Diagnostic(FuelRecommendations access) =>
    new(
      access.Stations.Any(x => x.ShortfallGallons > 0)
        ? FuelCalculationStatus.UnreachableStation
        : FuelCalculationStatus.NoFeasiblePlan,
      null,
      access
    );
}
