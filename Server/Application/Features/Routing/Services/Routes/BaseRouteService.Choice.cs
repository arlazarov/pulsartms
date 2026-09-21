using System.Text.Json;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class BaseRouteService
{
  private async Task<TruckRoute> ChosenAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    CancellationToken ct
  ) => (await ReadChosenAsync(load, profile, ct)).Route;

  private async Task<SavedRouteChoice> ReadChosenAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var saved = await db
      .DispatchRouteChoices.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId,
        ct
      );
    if (
      saved is null
      || saved.Revision != load.RouteChoiceRevision
      || saved.InputHash != ChoiceInputs(load, profile)
    )
      throw new RoutePlanningException(
        "Stops or truck restrictions changed. Review the selected route."
      );
    var choice = JsonSerializer.Deserialize<SavedRouteChoice>(
      saved.ChoiceJson,
      RoutingJson.Options
    );
    if (
      choice is null
      || !choice
        .Stops.Select(s => s.Id)
        .SequenceEqual(load.Stops.OrderBy(s => s.Sequence).Select(s => s.Id))
    )
      throw new RoutePlanningException(
        "The selected route no longer matches this load. Review the route."
      );
    RequireAnchored(choice.Route, choice.Stops.Select(s => s.Point).ToList());
    if (choice.Remaining is { } remaining)
    {
      var ids = choice.Stops.Select(s => s.Id).ToList();
      var indexes = remaining
        .Stops.Skip(1)
        .Select(s => ids.IndexOf(s.Id))
        .ToArray();
      if (
        remaining.Stops.Count < 2
        || remaining.Stops[0].Id != Guid.Empty
        || indexes.Any(i => i < 0)
        || indexes.Zip(indexes.Skip(1)).Any(pair => pair.Second <= pair.First)
      )
        throw new RoutePlanningException(
          "The remaining route no longer matches this load. Review the route."
        );
      RequireAnchored(
        remaining.Route,
        remaining.Stops.Select(s => s.Point).ToList()
      );
    }
    return choice;
  }

  public Task<TruckRoute> CurrentAsync(
    Load load,
    TruckRouteProfile profile,
    RoutePoint position,
    IReadOnlyList<PlanStop> remaining,
    CancellationToken ct
  ) =>
    CurrentAsync(
      RouteWorkProjection.Capture(load),
      profile,
      position,
      remaining,
      ct
    );

  public async Task<TruckRoute> CurrentAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    RoutePoint position,
    IReadOnlyList<PlanStop> remaining,
    CancellationToken ct
  )
  {
    if (load.RouteChoiceRevision == 0)
      return await routing.CalculateAsync(
        [position, .. remaining.Select(s => s.Point)],
        profile,
        ct
      );
    var work = RouteWorkProjection.TruckItinerary(load);
    var choice = await ReadChosenAsync(work, profile, ct);
    var selected = choice.Remaining?.Route ?? choice.Route;
    var ordered = (choice.Remaining?.Stops ?? choice.Stops)
      .Select(s => s.Id)
      .ToList();
    var indexes = remaining.Select(stop => ordered.IndexOf(stop.Id)).ToList();
    var first = indexes.Count > 0 ? indexes[0] : -1;
    if (
      first < 0
      || indexes.Zip(indexes.Skip(1)).Any(pair => pair.Second <= pair.First)
    )
      throw new RoutePlanningException(
        "Remaining stops changed. Review the selected route before recalculating."
      );
    RouteLeg? tail = null;
    RoutePoint join;
    if (first == 0)
      join = remaining[0].Point;
    else
    {
      var leg = selected.Legs[first - 1];
      tail = RouteViaGeometry.Remaining(leg, position);
      join = tail.Points[0];
    }
    var connector =
      RouteGeometry.Distance(position, join) < .001
        ? RouteViaGeometry.Join(
          [new(0, 0, [position, join])],
          [],
          DateTime.UtcNow
        )
        : await routing.CalculateAsync([position, join], profile, ct);
    RequireAnchored(connector, [position, join]);
    var firstLeg = tail is null
      ? connector.Legs[0]
      : RouteViaGeometry.JoinLegs([connector.Legs[0], tail]);
    var onward = Enumerable
      .Range(1, indexes.Count - 1)
      .Select(i =>
        RouteViaGeometry.JoinLegs(
          selected
            .Legs.Skip(indexes[i - 1])
            .Take(indexes[i] - indexes[i - 1])
            .ToList()
        )
      );
    var result = RouteViaGeometry.Join(
      [firstLeg, .. onward],
      selected.Warnings.Concat(connector.Warnings),
      connector.CalculatedAt
    );
    RequireAnchored(result, [position, .. remaining.Select(s => s.Point)]);
    return result;
  }
}
