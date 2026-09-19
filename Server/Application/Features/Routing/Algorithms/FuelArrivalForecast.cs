using Application.Features.Routing.Models;

namespace Application.Features.Routing.Algorithms;

public static class FuelArrivalForecast
{
  public static List<FuelStopArrival> Calculate(RoutePlanningState state)
  {
    if (
      state.Plan is not { InputsChanged: false } plan
      || plan.Tracking.AllStopsPassed
      || state.Progress
        is not {
          LocationStale: false,
          LocationTime: not null,
          Position.IsValid: true,
          ProgressMiles: { } progress,
        }
      || !double.IsFinite(progress)
      || progress < 0
    )
      return [];
    var passed = plan.Tracking.PassedStopIds.ToHashSet();
    var arrivals = plan.FuelPlan is { NeedsRefresh: false } fuel
      ? fuel
        .StopArrivals.Where(value =>
          value.DispatchId != plan.DispatchId || !passed.Contains(value.StopId)
        )
        .ToDictionary(value => (value.DispatchId, value.StopId))
      : [];
    if (
      state.Progress.OffRoute
      || state.FuelPercent is not { } fuelPercent
      || state.FuelUpdatedAt is not { } observedAt
      || observedAt > DateTime.UtcNow.AddMinutes(1)
      || fuelPercent is < 0 or > 100
      || state.Profile.Mpg is not > 0
      || state.Profile.TankGallons is not > 0
      || !double.IsFinite(fuelPercent)
      || !double.IsFinite(state.Profile.Mpg.Value)
      || !double.IsFinite(state.Profile.TankGallons.Value)
    )
      return arrivals.Values.ToList();
    var offset = plan.FromCurrentPosition ? 0 : 1;
    if (
      plan.Route.Legs.Count + offset != plan.Stops.Count
      || plan.Route.Legs.Any(leg =>
        !double.IsFinite(leg.Miles) || leg.Miles < 0
      )
    )
      return arrivals.Values.ToList();
    var gallons = fuelPercent / 100 * state.Profile.TankGallons.Value;
    double endMiles = 0;
    for (var index = 0; index < plan.Stops.Count; index++)
    {
      if (index >= offset)
      {
        var leg = plan.Route.Legs[index - offset];
        endMiles += leg.Miles;
      }
      var stop = plan.Stops[index];
      var key = (plan.DispatchId, stop.Id);
      if (passed.Contains(stop.Id) || arrivals.ContainsKey(key))
        continue;
      var remaining = Math.Max(0, endMiles - progress);
      var estimate = Math.Max(0, gallons - remaining / state.Profile.Mpg.Value);
      arrivals[key] = new(
        plan.DispatchId,
        stop.Id,
        estimate,
        estimate / state.Profile.TankGallons.Value * 100
      );
    }
    return arrivals.Values.ToList();
  }
}
