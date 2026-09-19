using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Addresses;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class RoutePlanningService
{
  internal async Task AddDisplayReferenceAsync(
    RoutePlan plan,
    RouteWorkSnapshot load,
    CancellationToken ct
  )
  {
    if (!plan.FromCurrentPosition || plan.ReferenceStops is not null)
      return;
    var reference = await ReadReferenceAsync(load, plan.Profile, ct);
    if (
      reference is not null
      && reference.Legs.Count > 0
      && reference.Legs.Count == load.Stops.Length - 1
      && reference.Legs.All(x => x.Points.Count > 0)
    )
    {
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
    }
  }

  private async Task<TruckRoute?> ReadReferenceAsync(
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
      ? SavedRouteReader.Route(full.RouteJson, load.Stops.Length - 1)
      : null;
  }
}
