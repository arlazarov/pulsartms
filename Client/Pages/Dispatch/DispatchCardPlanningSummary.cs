using Client.Models.DTO.Planning;

namespace Client.Pages.Dispatch;

public readonly record struct DispatchCardPlanningSummary(
  double? RemainingMiles,
  int? FuelStopCount
)
{
  public static DispatchCardPlanningSummary From(
    AutomaticPlanningResult? result,
    Guid? truckId,
    Guid? currentDispatchId,
    Guid? loadId
  )
  {
    if (
      truckId is not { } truck
      || truck == Guid.Empty
      || currentDispatchId is not { } current
      || current == Guid.Empty
      || loadId is not { } load
      || load == Guid.Empty
      || result is null
      || result.TruckId != truck
      || result.DispatchId != current
      || result.State?.Plan
        is not { InputsChanged: false, Tracking.AllStopsPassed: false } plan
      || plan.TruckId != truck
      || plan.DispatchId != current
    )
      return default;

    double? remaining =
      load == current
      && result.State.Progress?.RemainingMiles is { } miles
      && double.IsFinite(miles)
      && miles >= 0
        ? miles
        : null;
    int? count = null;
    if (
      plan.FuelPlan is { NeedsRefresh: false } fuel
      && fuel.TruckId == truck
      && fuel.DispatchIds.Contains(current)
      && fuel.DispatchIds.Contains(load)
    )
    {
      var visits = 0;
      foreach (var stop in fuel.Stops)
        if (stop.DispatchId == load)
          visits++;
      count = visits;
    }
    return new(remaining, count);
  }
}
