using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;

namespace Application.Features.Routing.Services.Routes;

public static class RouteDisplayReference
{
  public static async Task<TruckRoute?> ReconnectAsync(
    TruckRoute? reference,
    IReadOnlyList<PlanStop>? fullStops,
    IReadOnlyList<PlanStop> remaining,
    TruckRoute route,
    TruckRouteProfile profile,
    IRoutingProvider routing,
    CancellationToken ct
  )
  {
    if (reference is null || fullStops is null || remaining.Count == 0)
      return reference;
    var first = fullStops.ToList().FindIndex(s => s.Id == remaining[0].Id);
    if (
      first <= 0
      || !fullStops
        .Skip(first)
        .Select(s => s.Id)
        .SequenceEqual(remaining.Select(s => s.Id))
      || !RouteAnchoring.Matches(
        reference,
        fullStops.Select(s => s.Point).ToList()
      )
      || route.Legs.Count != remaining.Count
      || route.Legs[0].Points.Count < 2
    )
      return reference;
    var origin = route.Legs[0].Points[0];
    if (
      new RouteGeometry(reference).Match(origin).Away
      <= RouteAnchoring.ContinuityToleranceMiles
    )
      return reference;
    // Reconstruct planned road to GPS; this is not recorded travel history.
    TruckRoute prefix;
    try
    {
      prefix = await routing.CalculateAsync(
        [fullStops[first - 1].Point, origin],
        profile,
        ct
      );
    }
    catch (Exception error)
      when (error is HttpRequestException or RoutePlanningException
        || error is OperationCanceledException && !ct.IsCancellationRequested
      )
    {
      return reference;
    }
    if (
      !RouteAnchoring.Matches(prefix, [fullStops[first - 1].Point, origin])
      || !RouteAnchoring.Continuous(prefix, route)
    )
      return reference;
    return RouteViaGeometry.Join(
      [
        .. reference.Legs.Take(first - 1),
        RouteViaGeometry.JoinLegs([prefix.Legs[0], route.Legs[0]]),
        .. route.Legs.Skip(1),
      ],
      reference.Warnings.Concat(prefix.Warnings).Concat(route.Warnings),
      route.CalculatedAt
    );
  }
}
