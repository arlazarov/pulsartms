using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class BaseRouteService
{
  public async Task<TruckRoute> RecalculateAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    RoutePoint position,
    IReadOnlyList<PlanStop> remaining,
    RoutePlan previous,
    CancellationToken ct
  )
  {
    var first =
      remaining.Count == 0
        ? -1
        : previous.Stops.FindIndex(x => x.Id == remaining[0].Id);
    if (
      load.RouteChoiceRevision != 0
      || first < 0
      || remaining.Count < 2
      || !previous
        .Stops.Skip(first)
        .Select(x => x.Id)
        .SequenceEqual(remaining.Select(x => x.Id))
    )
      return await CurrentAsync(load, profile, position, remaining, ct);
    var offset = first + (previous.FromCurrentPosition ? 1 : 0);
    var suffix = previous.Route.Legs.Skip(offset).ToList();
    var tail = RouteViaGeometry.Join(
      suffix,
      previous.Route.Warnings,
      previous.Route.CalculatedAt
    );
    if (!RouteAnchoring.Matches(tail, remaining.Select(x => x.Point).ToList()))
      return await CurrentAsync(load, profile, position, remaining, ct);
    // A mandatory stop is an unambiguous reconnect boundary. Internal-road
    // joins need independent segment measures and must not rescale old miles.
    var connector = await routing.CalculateAsync(
      [position, remaining[0].Point],
      profile,
      ct
    );
    RequireAnchored(connector, [position, remaining[0].Point]);
    var result = RouteViaGeometry.Join(
      [connector.Legs[0], .. suffix],
      connector.Warnings.Concat(tail.Warnings),
      connector.CalculatedAt
    );
    RequireAnchored(result, [position, .. remaining.Select(x => x.Point)]);
    return result;
  }
}
