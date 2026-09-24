using Application.Features.Routing.Services.Addresses;
using Domain.Models.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class RoutePlanningService
{
  // The load's base road, as it was read: which row, from which inputs,
  // calculated when. A reference taken from it is saved only while this is
  // still the base road.
  internal sealed record BaseRoadStamp(
    Guid Id,
    string InputHash,
    DateTime CalculatedAt
  );

  internal async Task AddDisplayReferenceAsync(
    RoutePlan plan,
    RouteWorkSnapshot load,
    CancellationToken ct
  ) => await AttachReferenceAsync(plan, load, ct);

  // Gives a plan from the truck's position the load's base road as its
  // display reference, when the plan has none and the road is complete.
  // Returns the road it came from, or null when nothing was attached.
  private async Task<BaseRoadStamp?> AttachReferenceAsync(
    RoutePlan plan,
    RouteWorkSnapshot load,
    CancellationToken ct
  )
  {
    if (!plan.FromCurrentPosition || plan.ReferenceStops is not null)
      return null;
    var (reference, stamp) = await ReadReferenceAsync(load, plan.Profile, ct);
    if (
      reference is null
      || reference.Legs.Count == 0
      || reference.Legs.Count != load.Stops.Length - 1
      || reference.Legs.Any(x => x.Points.Count == 0)
    )
      return null;
    plan.ReferenceRoute = reference;
    plan.ReferenceStops = load
      .Stops.OrderBy(x => x.Sequence)
      .Select(
        (stop, index) =>
          EnrichStop(
            new(
              stop.Id,
              stop.Name,
              StopLocation.Address(stop),
              stop.Sequence,
              index == 0
                ? reference.Legs[0].Points[0]
                : reference.Legs[index - 1].Points[^1]
            ),
            load.Stops
          )
      )
      .ToList();
    return stamp;
  }

  private async Task<(
    TruckRoute? Route,
    BaseRoadStamp? Stamp
  )> ReadReferenceAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var full = await db
      .DispatchBaseRoutes.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId,
        ct
      );
    return full?.InputHash == BaseRouteService.Signature(load, profile)
      ? (
        SavedRouteReader.Route(full.RouteJson, load.Stops.Length - 1),
        new(full.Id, full.InputHash, full.CalculatedAt)
      )
      : (null, null);
  }

  private Task<bool> IsBaseRoadAsync(
    RouteWorkSnapshot load,
    BaseRoadStamp stamp,
    CancellationToken ct
  ) =>
    db
      .DispatchBaseRoutes.AsNoTracking()
      .AnyAsync(
        x =>
          x.DispatchId == load.Id
          && x.ExecutionLegId == load.ExecutionLegId
          && x.Id == stamp.Id
          && x.InputHash == stamp.InputHash
          && x.CalculatedAt == stamp.CalculatedAt,
        ct
      );
}
