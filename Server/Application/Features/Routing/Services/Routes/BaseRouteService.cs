using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Caching;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Addresses;
using Application.Models;
using Domain.Entities.Dispatch;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

public sealed partial class BaseRouteService(
  IAppDbContext db,
  IRoutingProvider routing,
  PlanningWorkPublication publication,
  TruckPlanningProfileService profiles,
  IPlanningPublicationScope publicationScope
)
{
  private static readonly KeyedGates Gates = new();

  public Task<TruckRoute> EnsureAsync(
    RouteWorkSnapshot work,
    TruckRouteProfile profile,
    CancellationToken ct,
    IReadOnlyList<RoutePoint>? resolvedPoints = null
  ) => EnsureCoreAsync(work, profile, profile, ct, resolvedPoints, null);

  public static string Signature(
    DispatchEntity load,
    TruckRouteProfile profile
  ) => Signature(RouteWorkProjection.Capture(load.TruckItinerary()), profile);

  public static string Signature(
    RouteWorkSnapshot work,
    TruckRouteProfile profile
  ) =>
    work.RouteChoiceRevision == 0
      ? ChoiceInputs(work, profile)
      : Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{ChoiceInputs(work, profile)}:{work.RouteChoiceRevision}"
          )
        )
      );

  public static string ChoiceInputs(
    DispatchEntity load,
    TruckRouteProfile profile
  ) =>
    ChoiceInputs(RouteWorkProjection.Capture(load.TruckItinerary()), profile);

  public static string ChoiceInputs(
    RouteWorkSnapshot work,
    TruckRouteProfile profile
  ) =>
    work.ExecutionLegId is { } legId
      ? Convert.ToHexString(
        SHA256.HashData(
          Encoding.UTF8.GetBytes(
            $"{legId}:{work.AssignmentRevision}:"
              + ItinerarySignature(work, profile)
          )
        )
      )
      : ItinerarySignature(work, profile);

  private static string ItinerarySignature(
    RouteWorkSnapshot load,
    TruckRouteProfile profile
  ) =>
    Convert.ToHexString(
      SHA256.HashData(
        Encoding.UTF8.GetBytes(
          JsonSerializer.Serialize(
            new
            {
              LocationPolicy = "google-street-address-v1",
              profile.HeightFeet,
              profile.WidthFeet,
              profile.LengthFeet,
              profile.WeightPounds,
              profile.Axles,
              profile.AxleWeightPounds,
              profile.Hazmat,
              Stops = load
                .Stops.OrderBy(s => s.Sequence)
                .Select(s => new
                {
                  s.Sequence,
                  Latitude = DecimalValue.Normalize(s.Latitude),
                  Longitude = DecimalValue.Normalize(s.Longitude),
                  s.Address,
                  s.City,
                  s.Province,
                  s.Country,
                  s.ZipCode,
                }),
            },
            RoutingJson.Options
          )
        )
      )
    );

  public Task<TruckRoute> EnsureAsync(
    DispatchEntity load,
    TruckRouteProfile profile,
    CancellationToken ct,
    IReadOnlyList<RoutePoint>? resolvedPoints = null
  ) =>
    EnsureCoreAsync(
      RouteWorkProjection.Capture(load.TruckItinerary()),
      profile,
      profile,
      ct,
      resolvedPoints,
      null
    );

  internal Task<TruckRoute> EnsureForBuildAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile requestedProfile,
    TruckRouteProfile observedProfile,
    TruckItinerarySnapshot capturedWork,
    CancellationToken ct
  ) =>
    EnsureCoreAsync(
      load,
      requestedProfile,
      observedProfile,
      ct,
      null,
      capturedWork
    );

  private async Task<TruckRoute> EnsureCoreAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile profile,
    TruckRouteProfile observedProfile,
    CancellationToken ct,
    IReadOnlyList<RoutePoint>? resolvedPoints,
    TruckItinerarySnapshot? capturedWork
  )
  {
    if (db.Database.CurrentTransaction is not null)
      throw new InvalidOperationException(
        "Base route preparation requires no active transaction."
      );
    load = RouteWorkProjection.TruckItinerary(load);
    profile = profile.Copy();
    observedProfile = observedProfile.Copy();
    resolvedPoints = resolvedPoints?.ToArray();
    if (profile.Validate() is { } error)
      throw new RoutePlanningException(error);
    await profiles.RequireRoutingCurrentAsync(load, observedProfile, ct);
    var ordered = load.Stops.OrderBy(s => s.Sequence).ToList();
    if (load.RouteChoiceRevision > 0)
      return await ChosenAsync(load, profile, ct);
    if (ordered.Count is < 2 or > 49)
      throw new RoutePlanningException(
        "The base route requires 2 to 49 stops."
      );
    if (
      resolvedPoints is not null
      && (
        resolvedPoints.Count != ordered.Count
        || resolvedPoints.Any(point => point?.IsValid != true)
      )
    )
      throw new RoutePlanningException(
        "The base route stop coordinates are incomplete."
      );
    var gate = Gates.For(load.ExecutionLegId ?? load.Id);
    await GateWait.WaitAsync(gate, "BaseRoute", ct);
    DispatchBaseRoute? saved = null;
    var ownsSaved = false;
    BaseRoadVersion? observedRoad = null;
    try
    {
      var hash = Signature(load, profile);
      saved = db.DispatchBaseRoutes.Local.FirstOrDefault(x =>
        x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId
      );
      ownsSaved = saved is null;
      saved ??= await db
        .DispatchBaseRoutes.AsNoTracking()
        .SingleOrDefaultAsync(
          x =>
            x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId,
          ct
        );
      observedRoad = saved is null
        ? null
        : new(saved.Id, saved.InputHash, saved.CalculatedAt);
      var cached =
        saved?.InputHash == hash
          ? SavedRouteReader.Route(saved.RouteJson, ordered.Count - 1)
          : null;
      var known =
        resolvedPoints
        ?? ordered
          .Select(stop =>
            stop.Latitude.HasValue && stop.Longitude.HasValue
              ? new RoutePoint((double)stop.Latitude, (double)stop.Longitude)
              : new RoutePoint(double.NaN, double.NaN)
          )
          .ToArray();
      if (cached is not null && RouteAnchoring.Matches(cached, known))
        return cached;
      var existing = await db
        .DispatchRoutePlans.AsNoTracking()
        .SingleOrDefaultAsync(
          x =>
            x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId,
          ct
        );
      var previous =
        existing?.InputHash == RoutePlanInputs.Hash(load, profile)
        && existing.TruckId == load.TruckId
          ? SavedRouteReader.Plan(existing.PlanJson)
          : null;
      var route =
        previous is { FromCurrentPosition: false }
        && previous.DispatchId == load.Id
        && previous.TruckId == load.TruckId
        && SavedRouteGeometry.Complete(previous.Route, ordered.Count - 1)
          ? previous.Route
          : null;
      if (route is null || !RouteAnchoring.Matches(route, known))
      {
        var points = resolvedPoints?.ToList() ?? [];
        if (resolvedPoints is null)
        {
          var locations = await StopLocation.ResolveAsync(
            load.Id,
            load.ExecutionLegId,
            ordered,
            db,
            routing,
            ct
          );
          points.AddRange(ordered.Select(stop => locations[stop.Id]));
        }
        route =
          cached is not null && RouteAnchoring.Matches(cached, points) ? cached
          : route is not null && RouteAnchoring.Matches(route, points) ? route
          : await RepairAsync(cached ?? route, points, profile, ct);
      }
      await using var publicationTransaction = capturedWork is null
        ? await publicationScope.BeginAsync(load.TruckId, ct)
        : await publication.BeginAsync(capturedWork, ct);
      await profiles.RequireRoutingCurrentAsync(load, observedProfile, ct);
      if (capturedWork is null)
        await RequireWorkCurrentAsync(load, profile, hash, ct);
      var currentRoad = await db
        .DispatchBaseRoutes.AsNoTracking()
        .Where(x =>
          x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId
        )
        .Select(x => new BaseRoadVersion(x.Id, x.InputHash, x.CalculatedAt))
        .SingleOrDefaultAsync(ct);
      if (currentRoad != observedRoad)
        throw new RoutePlanningException(
          "The saved base route changed during calculation. Refresh the route."
        );
      if (saved is null)
      {
        saved = new()
        {
          Id = Guid.NewGuid(),
          DispatchId = load.Id,
          ExecutionLegId = load.ExecutionLegId,
        };
        db.DispatchBaseRoutes.Add(saved);
      }
      else if (ownsSaved)
        db.DispatchBaseRoutes.Attach(saved);
      saved.InputHash = hash;
      saved.RouteJson = RoutePlanStorage.Serialize(route);
      saved.CalculatedAt = route.CalculatedAt;
      if (load.ExecutionLegId is { } executionLegId)
      {
        if (
          !await db.LockExecutionLegAsync(
            executionLegId,
            load.AssignmentRevision,
            ct
          )
        )
          throw new RoutePlanningException("The route assignment changed.");
        if (
          db.Entry(saved).State == EntityState.Added
          && await db
            .DispatchBaseRoutes.AsNoTracking()
            .AnyAsync(x => x.ExecutionLegId == executionLegId, ct)
        )
          throw new RoutePlanningException("The base route changed.");
      }
      ExecutionPlanningChanges.RouteSaved(
        db,
        load.Id,
        load.ExecutionLegId,
        load.TruckId,
        load.AssignmentRevision,
        DateTime.UtcNow
      );
      await db.SaveChangesAsync(ct);
      await publicationTransaction.CommitAsync(ct);
      return route;
    }
    finally
    {
      // A request may prepare many loads; only this operation's route entity is
      // disposable.
      try
      {
        if (ownsSaved && saved is not null)
        {
          db.Entry(saved).State = EntityState.Detached;
          saved.RouteJson = "";
        }
      }
      finally
      {
        gate.Release();
      }
    }
  }

  private sealed record BaseRoadVersion(
    Guid Id,
    string InputHash,
    DateTime CalculatedAt
  );

  private async Task<TruckRoute> RepairAsync(
    TruckRoute? saved,
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    if (saved is null)
    {
      var fresh = await routing.CalculateAsync(points, profile, ct);
      RequireAnchored(fresh, points);
      return fresh;
    }
    var legs = saved.Legs.ToList();
    var invalid = legs.Select(
        (leg, index) =>
          !RouteAnchoring.LegMatches(leg, points[index], points[index + 1])
      )
      .ToArray();
    for (var index = 1; index < legs.Count; index++)
      if (
        !invalid[index - 1]
        && !invalid[index]
        && !RouteAnchoring.Near(
          legs[index - 1].Points[^1],
          legs[index].Points[0],
          RouteAnchoring.ContinuityToleranceMiles
        )
      )
        invalid[index] = true;
    var warnings = saved.Warnings.ToList();
    var calculatedAt = saved.CalculatedAt;
    for (var first = 0; first < invalid.Length; first++)
    {
      if (!invalid[first])
        continue;
      var last = first;
      while (last + 1 < invalid.Length && invalid[last + 1])
        last++;
      var requested = points.Skip(first).Take(last - first + 2).ToList();
      if (first > 0)
        requested[0] = legs[first - 1].Points[^1];
      if (last + 1 < legs.Count)
        requested[^1] = legs[last + 1].Points[0];
      var repaired = await routing.CalculateAsync(requested, profile, ct);
      RequireAnchored(repaired, requested);
      for (var index = first; index <= last; index++)
        legs[index] = repaired.Legs[index - first];
      warnings.AddRange(repaired.Warnings);
      if (repaired.CalculatedAt < calculatedAt)
        calculatedAt = repaired.CalculatedAt;
      first = last;
    }
    var result = new TruckRoute
    {
      Legs = legs,
      Miles = legs.Sum(leg => leg.Miles),
      Seconds = legs.Sum(leg => leg.Seconds),
      Points = legs.SelectMany(
          (leg, index) => index == 0 ? leg.Points : leg.Points.Skip(1)
        )
        .ToList(),
      Warnings = warnings.Distinct().ToList(),
      CalculatedAt = calculatedAt,
    };
    RequireAnchored(result, points);
    return result;
  }

  private static void RequireAnchored(
    TruckRoute route,
    IReadOnlyList<RoutePoint> points
  )
  {
    if (!SavedRouteGeometry.Complete(route, points.Count - 1))
      throw new RoutePlanningException(
        "The base route geometry is incomplete."
      );
    if (!RouteAnchoring.Matches(route, points))
      throw new RoutePlanningException(
        "The base route does not reach the confirmed stops. The saved route has been kept."
      );
  }
}
