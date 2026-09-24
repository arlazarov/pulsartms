using Application.Features.Routing.Services.Addresses;
using Domain.Models.Routing;

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

  // The load's base road, as it is now, in one round trip: the road's text
  // is left out when the kept copy has this row's revision. The revision
  // and the text come from the same row read, so a copy is never kept under
  // a revision it does not belong to.
  private async Task<TruckRoute?> ReadReferenceAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var key = (
      Load: load.Id,
      Leg: load.ExecutionLegId,
      Legs: load.Stops.Length - 1
    );
    var kept = displays.FindReference(key);
    var keptRow = kept?.Row ?? Guid.Empty;
    var keptRevision = kept?.Revision ?? -1;
    var row = await db
      .DispatchBaseRoutes.AsNoTracking()
      .Where(x =>
        x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId
      )
      .Select(x => new
      {
        x.CompanyId,
        x.Id,
        x.InputHash,
        x.Revision,
        RouteJson = x.Id == keptRow && x.Revision == keptRevision
          ? null
          : x.RouteJson,
      })
      .SingleOrDefaultAsync(ct);
    if (
      row is null
      || row.InputHash != BaseRouteService.Signature(load, profile)
    )
      return null;
    if (row.RouteJson is null)
      return kept!.Is(row.CompanyId, row.Id, row.Revision)
        ? kept.Route()
        : null;
    var reference = new RouteDisplayCache.DisplayReference(
      row.CompanyId,
      row.Id,
      row.Revision,
      SavedRouteReader.Route(row.RouteJson, key.Legs)
    );
    displays.KeepReference(key, reference);
    return reference.Route();
  }
}
