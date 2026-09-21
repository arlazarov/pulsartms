using System.Collections.Immutable;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Eta;
using Domain.Models.Routing;
using Domain.Rules.Eta;

namespace Application.Features.Eta.Services;

// Turning a described chain into a plan: the saved roads behind the loads
// ahead are read once, compiled into travel times, and each load is handed
// either its timing or the sentence explaining what is still missing.
public sealed partial class EtaChainInputsService
{
  private async Task<
    IReadOnlyDictionary<Guid, SavedNextLoadRoute>
  > FutureGeometryAsync(
    IReadOnlyList<RouteWorkSnapshot> future,
    CancellationToken ct
  )
  {
    var values = new Dictionary<Guid, SavedNextLoadRoute>();
    if (Plain(future) is { Length: > 0 } plain)
      foreach (
        var (id, route) in await savedRoutes.ReadGeometryAsync(plain, ct)
      )
        values[id] = route;
    // Asked for by leg and answered by leg; the chain knows loads, so the
    // answer is put back under the load it belongs to.
    if (Accepted(future) is { Length: > 0 } legs)
      foreach (
        var route in (
          await savedRoutes.ReadExecutionGeometryAsync(legs, ct)
        ).Values
      )
        values[route.DispatchId] = route;
    return values;
  }

  public async Task<EtaChainPlan> PrepareAsync(
    EtaChainDescription description,
    CancellationToken ct
  )
  {
    var timings = await memory.FutureTimingAsync(
      description.GeometryHash,
      () => FutureTimingAsync(description, ct),
      ct
    );
    var byId = timings.ToDictionary(x => x.DispatchId);
    var result = description
      .Loads.Skip(1)
      .Select(load => Future(description, load, byId[load.Id]))
      .ToArray();
    return new(
      description.InputHash,
      result,
      description
        .Loads[0]
        .Stops.ToDictionary(
          s => s.Id,
          s => new EtaStopActivity(
            s.ArrivedAt,
            s.PickedUpAt,
            s.DeliveredAt,
            s.DepartedAt,
            s.ManualCompletedAt,
            s.CompletionOverride
          )
        )
    )
    {
      CurrentUnavailableReason =
        EtaChainReadiness.SequenceReason(
          description.Sequence,
          description.Loads[0]
        )
        ?? (
          EtaChainReadiness.DriverChanged(
            description.Loads[0],
            description.DriverId
          )
            ? EtaChainReadiness.CurrentDriverChanged
            : null
        ),
    };
  }

  private async Task<ImmutableArray<EtaFutureTiming>> FutureTimingAsync(
    EtaChainDescription description,
    CancellationToken ct
  )
  {
    var future = description.Loads.Skip(1).ToArray();
    var saved = await FutureGeometryAsync(future, ct);
    RequireFutureRoads(
      description.Roads.Future,
      future.Select(load =>
        NextLoadRouteVersion.From(
          load.Id,
          load.ExecutionLegId,
          saved.GetValueOrDefault(load.Id)
        )
      )
    );
    var values = new List<EtaFutureTiming>();
    var previous = description.RootDispatchId;
    foreach (var load in future)
    {
      values.Add(
        Timing(description, load, saved.GetValueOrDefault(load.Id), previous)
      );
      previous = load.Id;
    }
    return values.ToImmutableArray();
  }

  private EtaFutureTiming Timing(
    EtaChainDescription description,
    RouteWorkSnapshot load,
    SavedNextLoadRoute? item,
    Guid previous
  )
  {
    var pair = description.Connections.GetValueOrDefault(load.Id);
    var connection =
      pair?.Previous.Id == previous
        ? pair.ReadRoute(item?.Deadhead, description.Profile)
        : null;
    var road =
      item?.BaseRoute is { } baseRoute
      && baseRoute.InputHash
        == BaseRouteService.Signature(load, description.Profile)
        ? SavedRouteReader.Route(baseRoute.RouteJson, load.Stops.Length - 1)
        : null;
    string? reason =
      connection is null ? EtaChainReadiness.MissingConnection
      : road is null ? EtaChainReadiness.MissingRoad
      : null;
    var routeTiming = road is null
      ? null
      : EtaRouteTiming.Compile(road, regions);
    var connectionTiming =
      connection is null
      || connection.Legs.All(leg => leg.Miles == 0 && leg.Seconds == 0)
        ? null
        : EtaRouteTiming.Compile(connection, regions);
    if (
      routeTiming?.HasCompleteTravelTimes == false
      || connectionTiming?.HasCompleteTravelTimes == false
    )
      reason = EtaChainReadiness.IncompleteTravelTimes;
    var points =
      road is null || road.Legs.Count == 0
        ? ImmutableArray<RoutePoint>.Empty
        : road
          .Legs.Select(leg => leg.Points[^1])
          .Prepend(road.Legs[0].Points[0])
          .ToImmutableArray();
    return new(load.Id, connectionTiming, routeTiming, points, reason);
  }

  private static EtaFutureDispatch Future(
    EtaChainDescription description,
    RouteWorkSnapshot load,
    EtaFutureTiming value
  )
  {
    var stops = load
      .Stops.OrderBy(s => s.Sequence)
      .Select(
        (s, i) =>
          new PlanStop(
            s.Id,
            s.Name,
            s.Address,
            s.Sequence,
            i < value.StopPoints.Length ? value.StopPoints[i] : new(0, 0)
          )
          {
            Job = s.Job,
            StateAfter = s.StateAfter,
            ScheduledDate = s.ScheduledDate,
            ScheduledTime = s.ScheduledTime,
            ScheduledDate2 = s.ScheduledDate2,
            ScheduledTime2 = s.ScheduledTime2,
            AppointmentTimeZoneId = s.AppointmentTimeZoneId,
          }
      )
      .ToArray();
    return new(
      load.Id,
      stops,
      value.Connection,
      value.Route,
      EtaChainReadiness.SequenceReason(description.Sequence, load)
        ?? (
          EtaChainReadiness.DriverChanged(load, description.DriverId)
            ? EtaChainReadiness.NextLoadDriverChanged
            : value.UnavailableReason
        )
    );
  }
}
