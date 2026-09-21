using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class BaseRouteService
{
  private async Task RequireWorkCurrentAsync(
    RouteWorkSnapshot expected,
    TruckRouteProfile profile,
    string signature,
    CancellationToken ct
  )
  {
    var source = await db
      .Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleOrDefaultAsync(x => x.Id == expected.Id, ct);
    RouteWorkSnapshot? current = null;
    if (source is not null)
    {
      var accepted = await ExecutionRouteSections.ReadAsync(db, [source], ct);
      current =
        accepted.TryGetValue(source.Id, out var sections)
          ? sections.SingleOrDefault(x =>
            x.ExecutionLegId == expected.ExecutionLegId
          )
        : expected.ExecutionLegId is null
          ? RouteWorkProjection.Capture(source.TruckItinerary())
        : null;
    }
    if (
      current is { TruckId: null }
      && expected.TruckId.HasValue
      && !string.IsNullOrWhiteSpace(current.TruckNumber)
    )
      current = current with
      {
        TruckId = await db
          .Trucks.Where(x => x.UnitNumber == current.TruckNumber)
          .Select(x => (Guid?)x.Id)
          .SingleOrDefaultAsync(ct),
      };
    if (
      current is null
      || current.TruckId != expected.TruckId
      || expected.ExecutionLegId is null
        && current.PlanningAssignmentRevision
          != expected.PlanningAssignmentRevision
      || Signature(current, profile) != signature
    )
      throw new RoutePlanningException(
        "The base route work changed during calculation. Refresh the route."
      );
  }
}
