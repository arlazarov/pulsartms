using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq.Expressions;
using Application.Diagnostics;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

// What the horizon reads, and when. The loop over the following loads used
// to fetch each load's connection and base route as it reached it; these are
// the same reads, gathered before it starts.
public sealed partial class FuelHorizon
{
  public readonly record struct SavedKey(Guid Dispatch, Guid? Leg);

  // The rows the loop below will ask for, read before it starts.
  //
  // Each iteration used to fetch its own connection and its own base route,
  // one round trip each, and the loop runs once per following load: four
  // round trips for two loads, every one of them carrying route geometry.
  // The keys are all known beforehand, so they are read together instead.
  //
  // Which loads qualify is decided here by the loop's own conditions, using
  // only what is already in memory: a load the loop skips is not read for, and
  // collection stops where the loop would throw, so nothing past that point is
  // read either.
  //
  // It does not follow that a rejected itinerary costs no reads. The loads
  // before the break are collected and read, and the loop's later refusals -
  // a missing connection, geometry that does not join, an unanchored route -
  // are decided after the reads have happened, exactly as before.
  //
  // A dictionary entry exists for every key that was looked for, holding null
  // when that row does not exist, so "no entry" means the prefetch did not
  // cover this key and the read falls back to its own query. The two reads are
  // sequential: they share one DbContext.
  private async Task<(
    Dictionary<SavedKey, DispatchDeadhead?> Connections,
    Dictionary<SavedKey, DispatchBaseRoute?> Bases
  )> ReadAheadAsync(
    IReadOnlyList<RouteWorkSnapshot> following,
    Guid truckId,
    int stopsSoFar,
    CancellationToken ct
  )
  {
    var (connections, bases) = KeysToReadAhead(following, truckId, stopsSoFar);
    // Sequential, not concurrent: they share one DbContext.
    return (
      await ReadConnectionRowsAsync(connections, ct),
      await ReadBaseRowsAsync(bases, ct)
    );
  }

  // Which of the following loads the loop will read for, by the loop's own
  // conditions and in its order. It uses nothing but what is already in
  // memory, and it stops where the loop would throw rather than reading past
  // the point the horizon gives up - the prefix before that point is still
  // collected, and still read.
  public static (
    List<SavedKey> Connections,
    List<SavedKey> Bases
  ) KeysToReadAhead(
    IReadOnlyList<RouteWorkSnapshot> following,
    Guid truckId,
    int stopsSoFar
  )
  {
    var connections = new List<SavedKey>();
    var bases = new List<SavedKey>();
    var running = stopsSoFar;
    foreach (var load in following)
    {
      if (
        load.TruckId != truckId
        || load.Stops.Any(s => s.TruckId.HasValue && s.TruckId != truckId)
      )
        break;
      var ordered = load.Stops.OrderBy(s => s.Sequence).ToList();
      var remaining = ordered.Count(s => !s.IsCompleted);
      if (remaining == 0)
        continue;
      if (running + remaining > 40)
        break;
      running += remaining;
      var key = new SavedKey(load.Id, load.ExecutionLegId);
      connections.Add(key);
      if (ordered.Count > 1)
        bases.Add(key);
    }
    return (connections, bases);
  }

  public async Task<
    Dictionary<SavedKey, DispatchDeadhead?>
  > ReadConnectionRowsAsync(IReadOnlyList<SavedKey> keys, CancellationToken ct)
  {
    if (keys.Count == 0)
      return [];
    // The query is timed here rather than at the loop. Moving these reads out
    // of the loop took the round trip out of the "connection" and "base"
    // stages, which still cover everything else those calls do - reading the
    // prefetched row, the freshness comparison, deserialising the geometry,
    // and the fallback query when a key was not covered.
    var reading = Stopwatch.GetTimestamp();
    var rows = await db
      .DispatchDeadheads.AsNoTracking()
      .Where(ExactPairs<DispatchDeadhead>(keys))
      .ToListAsync(ct);
    PerformanceStages.Elapsed("fuel-horizon", "connection-rows-query", reading);
    return Pair(
      "connection-rows-read",
      keys,
      rows,
      x => new SavedKey(x.DispatchId, x.ExecutionLegId)
    );
  }

  public async Task<Dictionary<SavedKey, DispatchBaseRoute?>> ReadBaseRowsAsync(
    IReadOnlyList<SavedKey> keys,
    CancellationToken ct
  )
  {
    if (keys.Count == 0)
      return [];
    var reading = Stopwatch.GetTimestamp();
    var rows = await db
      .DispatchBaseRoutes.AsNoTracking()
      .Where(ExactPairs<DispatchBaseRoute>(keys))
      .ToListAsync(ct);
    PerformanceStages.Elapsed("fuel-horizon", "base-rows-query", reading);
    return Pair(
      "base-rows-read",
      keys,
      rows,
      x => new SavedKey(x.DispatchId, x.ExecutionLegId)
    );
  }

  // One entry per key that was asked for, null when that row does not exist,
  // so a caller can tell "covered, absent" from "not covered" and only the
  // second falls back to its own query. Rows that match no key are dropped.
  //
  // How many rows came back is counted, because dropping them here would hide
  // the thing worth knowing: these tables carry route geometry, and a filter
  // that fetched a neighbouring row would cost its payload and then throw it
  // away silently. The count should equal the number of keys that had a row.
  private static Dictionary<SavedKey, T?> Pair<T>(
    string stage,
    IReadOnlyList<SavedKey> keys,
    IReadOnlyList<T> rows,
    Func<T, SavedKey> keyOf
  )
    where T : class
  {
    PerformanceStages.Count("fuel-horizon", stage, rows.Count);
    var found = rows.ToDictionary(keyOf);
    return keys.Distinct().ToDictionary(key => key, found.GetValueOrDefault);
  }

  // The pairs themselves, one disjunction per key, rather than a list of legs
  // and a list of dispatches.
  //
  // A list of legs looked exact, because a leg is unique across its table. It
  // is not: uniqueness says the leg names one row, not that the row belongs to
  // the dispatch that was asked about. Given the pair (A, legOfB) the query
  // would have returned B's row, and dropping it afterwards is too late - by
  // then its RouteJson, which averages half a megabyte, has crossed the wire.
  // Asking for the pair means a row for another dispatch never leaves the
  // database.
  //
  // Both entity types name these two columns the same way, which is what the
  // property lookup below relies on.
  public static Expression<Func<T, bool>> ExactPairs<T>(
    IReadOnlyList<SavedKey> keys
  )
  {
    var row = Expression.Parameter(typeof(T), "row");
    var dispatch = Expression.Property(
      row,
      nameof(DispatchBaseRoute.DispatchId)
    );
    var leg = Expression.Property(
      row,
      nameof(DispatchBaseRoute.ExecutionLegId)
    );
    Expression? matched = null;
    foreach (var key in keys.Distinct())
    {
      // Field access on a held value, not Expression.Constant(guid). This is
      // the shape the compiler emits for a captured local, and it is what EF
      // turns into a parameter: written as a bare constant the identifiers
      // were inlined into the SQL text, so every distinct set of loads
      // produced a different statement and missed both EF's query cache and
      // the server's plan cache. Verified by reading the generated SQL.
      var held = Expression.Constant(new Held(key.Dispatch, key.Leg));
      var pair = Expression.AndAlso(
        Expression.Equal(
          dispatch,
          Expression.Field(held, nameof(Held.Dispatch))
        ),
        Expression.Equal(leg, Expression.Field(held, nameof(Held.Leg)))
      );
      matched = matched is null ? pair : Expression.OrElse(matched, pair);
    }
    return Expression.Lambda<Func<T, bool>>(matched!, row);
  }

  private sealed class Held(Guid dispatch, Guid? leg)
  {
    public readonly Guid Dispatch = dispatch;
    public readonly Guid? Leg = leg;
  }

  private async Task<(
    TruckRoute? Route,
    DeadheadHistoryBatch? History,
    SavedRoadVersion Road
  )> ReadConnectionAsync(
    RouteWorkSnapshot previous,
    RouteWorkSnapshot next,
    TruckRouteProfile profile,
    IReadOnlyDictionary<SavedKey, DispatchDeadhead?> prefetched,
    CancellationToken ct
  )
  {
    if (!previous.ExecutionLegId.HasValue)
    {
      // The two branches are recorded apart: which one a load takes depends on
      // whether its predecessor has an execution leg, and they do different
      // work. A missing row means that branch was not taken.
      var capturing = Stopwatch.GetTimestamp();
      var captured = await deadheads.CaptureRouteAsync(
        previous.Id,
        next,
        profile,
        ct
      );
      PerformanceStages.Elapsed("fuel-horizon", "capture-route", capturing);
      return captured;
    }
    // The captured itinerary selected this predecessor. Its execution owns
    // the delivery assignment even when the imported load retains old trucks.
    var history = new Dictionary<Guid, DeadheadHistorySnapshot>
    {
      [next.Id] = DeadheadHistoryProjection.Capture(
        new(next, [previous], false)
      ),
    };
    var rereading = Stopwatch.GetTimestamp();
    // The prefetch holds an entry for every key it looked for, null included,
    // so "has an entry" means it was covered. A key it did not cover falls
    // back to the read's own query rather than being treated as absent.
    var key = new SavedKey(next.Id, next.ExecutionLegId);
    var saved = prefetched.TryGetValue(key, out var row)
      ? deadheads.ReadCapturedRoute(previous.Id, next, profile, history, row)
      : await deadheads.ReadCapturedRouteAsync(
        previous.Id,
        next,
        profile,
        history,
        ct
      );
    PerformanceStages.Elapsed("fuel-horizon", "read-captured-route", rereading);
    return (saved.Route, null, saved.Road);
  }

  private async Task<(
    TruckRoute Route,
    ImmutableArray<SavedRoadVersion> Roads
  )> ReadBaseAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    IReadOnlyList<RoutePoint> points,
    IReadOnlyDictionary<SavedKey, DispatchBaseRoute?> prefetched,
    CancellationToken ct
  )
  {
    var basing = Stopwatch.GetTimestamp();
    // Covered by the prefetch, or read here. An entry exists for every key it
    // looked for, null included, so a key it did not cover still gets its own
    // query. The hash comparison below is untouched either way.
    var key = new SavedKey(load.Id, load.ExecutionLegId);
    var saved = prefetched.TryGetValue(key, out var row)
      ? row
      : await db
        .DispatchBaseRoutes.AsNoTracking()
        .SingleOrDefaultAsync(
          existing =>
            existing.DispatchId == load.Id
            && existing.ExecutionLegId == load.ExecutionLegId,
          ct
        );
    PerformanceStages.Elapsed("fuel-horizon", "base-route-read", basing);
    var roads = ImmutableArray.CreateBuilder<SavedRoadVersion>();
    roads.Add(
      SavedRoadVersion.Base(
        NextLoadRouteVersion.From(
          load.Id,
          load.ExecutionLegId,
          new(load.Id, saved, null)
        )
      )
    );
    var route =
      saved?.InputHash == BaseRouteService.Signature(load, profile)
        ? SavedRouteReader.Route(saved.RouteJson, points.Count - 1)
        : null;
    if (route is null)
    {
      var loading = Stopwatch.GetTimestamp();
      var stored = await db
        .DispatchRoutePlans.AsNoTracking()
        .SingleOrDefaultAsync(
          row =>
            row.DispatchId == load.Id
            && row.ExecutionLegId == load.ExecutionLegId,
          ct
        );
      await RoutePlanStorage.LoadAsync(db, stored, ct);
      // The row and its chunked geometry together - one is worthless without
      // the other, and the load is where the geometry actually arrives.
      PerformanceStages.Elapsed("fuel-horizon", "base-plan-read", loading);
      var previous =
        stored?.InputHash == RoutePlanInputs.Hash(load, profile)
        && stored.TruckId == load.TruckId
        && stored.AssignmentRevision == load.AssignmentRevision
          ? RoutePlanStorage.Read(stored)
          : null;
      if (
        previous is { FromCurrentPosition: false }
        && previous.DispatchId == load.Id
        && previous.TruckId == load.TruckId
        && previous.ExecutionLegId == load.ExecutionLegId
        && previous.AssignmentRevision == load.AssignmentRevision
      )
      {
        route = previous.Route;
        roads.Add(SavedRoadVersion.Plan(stored!, previous));
      }
    }
    if (route is null)
      throw new RoutePlanningException(
        "A matching saved base route is required for every assigned load before finding fuel. The saved fuel plan has been kept."
      );
    FuelHorizonRoad.RequireAnchored(route, points);
    return (route, roads.ToImmutable());
  }
}
