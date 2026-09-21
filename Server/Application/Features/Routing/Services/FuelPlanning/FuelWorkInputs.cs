using Application.Features.Routing.Services.Routes;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed record FuelWorkInputs(TruckItinerarySnapshot Itinerary)
{
  public List<RouteWorkSnapshot> Select(RoutePlan plan)
  {
    if (plan.TruckId != Itinerary.TruckId)
      throw new RoutePlanningException("The truck assignment changed.");
    var candidates = Itinerary.Segments.Where(x =>
      x.Work.ExecutionLegId.HasValue || x.Status is "assigned" or "in_transit"
    );
    var selected = FuelHorizonLoads.SelectLoads(
      plan,
      candidates
        .Select(x =>
          RouteWorkProjection.Capture(x, Itinerary.Resources.TruckNumber)
        )
        .ToArray()
    );
    foreach (var load in selected)
      _ = Resolve(load);
    return selected;
  }

  public List<RouteWorkSnapshot> SelectForDisplay(RoutePlan plan)
  {
    try
    {
      return Select(plan);
    }
    catch (RoutePlanningException)
    {
      return [];
    }
  }

  internal RouteWorkSnapshot Root(RoutePlan plan) =>
    Resolve(
      Select(plan)
        .Single(x =>
          x.Id == plan.DispatchId && x.ExecutionLegId == plan.ExecutionLegId
        )
    );

  internal RouteWorkSnapshot Resolve(RouteWorkSnapshot identity)
  {
    var segment = Itinerary.Segments.Single(x =>
      x.Work.DispatchId == identity.Id
      && x.Work.ExecutionLegId == identity.ExecutionLegId
    );
    return PlanningWorkPolicy.Resolve(Itinerary, segment);
  }
}
