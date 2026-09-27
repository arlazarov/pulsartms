using Application.Features.Routing.Services.Routes;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed record FuelWorkInputs(TruckItinerarySnapshot Itinerary)
{
  public List<RouteWorkSnapshot> Select(RoutePlan plan) => Select(plan, out _);

  internal List<RouteWorkSnapshot> Select(
    RoutePlan plan,
    out string? coverageNotice
  )
  {
    coverageNotice = null;
    if (plan.TruckId != Itinerary.TruckId)
      throw new RoutePlanningException("The truck assignment changed.");
    var candidates = Itinerary
      .Segments.Where(x =>
        x.Work.ExecutionLegId.HasValue || x.Status is "assigned" or "in_transit"
      )
      .ToList();
    var root = candidates.FindIndex(x =>
      x.Work.DispatchId == plan.DispatchId
      && x.Work.ExecutionLegId == plan.ExecutionLegId
    );
    if (plan.ExecutionLegId.HasValue && root >= 0)
    {
      var boundary = candidates.FindIndex(
        root + 1,
        x =>
          !x.Work.ExecutionLegId.HasValue
          && x.Problems.Contains(WorkReadProblem.SourceReviewRequired)
      );
      if (boundary >= 0)
      {
        coverageNotice =
          $"Fuel coverage ends before load {candidates[boundary].LoadNumber}, "
          + "which needs assignment review. That load and later work are not included.";
        candidates.RemoveRange(boundary, candidates.Count - boundary);
      }
    }
    var selected = FuelHorizonLoads.SelectLoads(
      plan,
      candidates
        .Select(x =>
          RouteWorkProjection.Capture(x, Itinerary.Resources.TruckNumber)
        )
        .ToArray()
    );
    if (
      coverageNotice is not null
      && (
        !FuelHorizonLoads.EndsAtDelivery(selected[^1])
        || selected[^1].Id != candidates[^1].Work.DispatchId
      )
    )
      coverageNotice = null;
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
