using Application.Features.Routing.Models;

namespace Application.Features.Routing.Interfaces;

// Where the truck observation behind a read may come from.
public enum PlannedRouteTelemetry
{
  // The provider may be consulted where the GPS eligibility rule allows it.
  Live,

  // The latest known observation, read even where the rule would skip it.
  // An editor needs the truck reading it is editing against.
  Cached,

  // The latest known observation, eligibility rule unchanged. A caller
  // inside an open transaction uses this so it never waits for a provider.
  WithoutProviderWait,
}

// What fuel planning needs from the route lifecycle, stated as its own
// contract so it does not reach into the service that owns that lifecycle.
// Reads only: nothing here writes or publishes.
public interface IPlannedRouteReader
{
  Task<RoutePlanningState> GetAsync(
    RouteWorkSnapshot work,
    CancellationToken ct,
    PlannedRouteTelemetry telemetry = PlannedRouteTelemetry.Live
  );

  Task<RouteWorkSnapshot> LoadAsync(
    Guid dispatchId,
    CancellationToken ct,
    Guid? executionLegId = null
  );

  Task<TruckRouteProfile> ProfileAsync(Guid truckId, CancellationToken ct);
}
