using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Options;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

// Building a road for a truck's work, from its first stop or from where it
// stands, and publishing it with the profile it was built for.
public sealed partial class RoutePlanningService
{
  public async Task<RoutePlan> BuildAsync(
    Guid dispatchId,
    RouteBuildRequest request,
    CancellationToken ct,
    bool automatic = false,
    TruckItinerarySnapshot? capturedWork = null
  )
  {
    if (request.Profile.Validate() is { } error)
      throw new RoutePlanningException(error);
    var work = capturedWork;
    if (work is null)
    {
      var captured = await CaptureWorkAsync(
        dispatchId,
        ct,
        request.ExecutionLegId
      );
      work = captured.Itinerary;
      request = request with { ExecutionLegId = captured.ExecutionLegId };
    }
    var gate = BuildGates.For(work.TruckId);
    await GateWait.WaitAsync(gate, "RouteBuild", ct);
    try
    {
      await inputs.RequireCurrentAsync(work, ct);
      var observedProfile = automatic
        ? request.Profile
        : await profiles.GetUncachedAsync(work.TruckId, ct);
      if (automatic)
        await profiles.RequireCurrentAsync(work.TruckId, observedProfile, ct);
      var load = PlanningWorkPolicy.Resolve(
        work,
        dispatchId,
        request.ExecutionLegId
      );
      if (request.FromCurrentPosition && !PlanningWorkPolicy.CanUseGps(load))
        throw new RoutePlanningException(
          "Confirm receipt before routing this leg from the truck GPS."
        );
      var hash = RoutePlanInputs.Hash(load, request.Profile);
      var entity = await store.ReadForUpdateAsync(
        dispatchId,
        ct,
        load.ExecutionLegId
      );
      var old = entity is null ? null : RoutePlanStorage.Read(entity);
      var ordered = load.Stops.OrderBy(x => x.Sequence).ToList();
      if (request.FromCurrentPosition)
      {
        if (
          !request.NextStopSequence.HasValue
          || !ordered.Any(x => x.Sequence == request.NextStopSequence)
        )
          throw new RoutePlanningException(
            "Choose the next uncompleted stop before routing from the current position."
          );
        ordered = ordered
          .Where(x => x.Sequence >= request.NextStopSequence && !x.IsCompleted)
          .ToList();
      }
      if (ordered.Count < (request.FromCurrentPosition ? 1 : 2))
        throw new RoutePlanningException(
          "This load has too few stops to build a route."
        );
      if (ordered.Count > 49)
        throw new RoutePlanningException("This route supports up to 49 stops.");
      if (
        old is not null
        && RoutePlanInputs.Matches(entity!, load, request.Profile)
        && !request.FromCurrentPosition
        && !old.FromCurrentPosition
      )
        return old;
      if (
        old is not null
        && RoutePlanInputs.Matches(entity!, load, request.Profile)
        && old.CalculatedAt > DateTime.UtcNow.AddSeconds(-60)
      )
        throw new RoutePlanningException(
          "The route was just calculated. Wait one minute before rebuilding it."
        );
      var stops = new List<PlanStop>();
      var locations = await StopLocation.ResolveAsync(
        load.Id,
        load.ExecutionLegId,
        ordered,
        db,
        routing,
        ct
      );
      var capturedStops = load.Stops;
      foreach (var stop in ordered)
      {
        var address = StopLocation.Address(stop);
        var point = locations[stop.Id];
        stops.Add(
          EnrichStop(
            new(stop.Id, stop.Name, address, stop.Sequence, point),
            capturedStops
          )
        );
      }
      var points = stops.Select(x => x.Point).ToList();
      if (request.FromCurrentPosition)
      {
        var current = await LocationAsync(load.TruckId!.Value, ct);
        if (
          current is null
          || DateTime.UtcNow - current.UpdatedAt > TimeSpan.FromMinutes(10)
        )
          throw new RoutePlanningException(
            "A fresh truck GPS location is needed to route from its current position."
          );
        points.Insert(
          0,
          new((double)current.Latitude, (double)current.Longitude)
        );
        if (automatic)
          await recalculationBudget.ReserveAsync(
            load.TruckId.Value,
            points[0],
            ct
          );
      }
      var route = !request.FromCurrentPosition
        ? await baseRoutes.EnsureForBuildAsync(
          load,
          request.Profile,
          observedProfile,
          work,
          ct
        )
        : await baseRoutes.CurrentAsync(
          load,
          request.Profile,
          points[0],
          stops,
          ct
        );
      TruckRoute? reference = null;
      if (request.FromCurrentPosition)
      {
        if (
          old is not null
          && RoutePlanInputs.Matches(entity!, load, request.Profile)
        )
          reference =
            old.ReferenceRoute ?? (old.FromCurrentPosition ? null : old.Route);
        if (reference is null)
        {
          reference = await ReadReferenceAsync(load, request.Profile, ct);
        }
      }
      var plan = new RoutePlan
      {
        Id = entity?.Id ?? Guid.NewGuid(),
        DispatchId = dispatchId,
        ExecutionLegId = load.ExecutionLegId,
        AssignmentRevision = load.AssignmentRevision,
        TruckId = load.TruckId!.Value,
        Version = (old?.Version ?? 0) + 1,
        CalculatedAt = route.CalculatedAt,
        Profile = request.Profile,
        OriginalPlannedMiles = old?.OriginalPlannedMiles ?? route.Miles,
        FromCurrentPosition = request.FromCurrentPosition,
        Stops = stops,
        Route = route,
        ReferenceRoute = reference,
        FuelPlan = old?.FuelPlan,
      };
      if (plan.FromCurrentPosition)
      {
        await AddDisplayReferenceAsync(plan, load, ct);
        plan.ReferenceRoute = await RouteDisplayReference.ReconnectAsync(
          plan.ReferenceRoute,
          plan.ReferenceStops,
          plan.Stops,
          plan.Route,
          plan.Profile,
          routing,
          ct
        );
      }
      if (plan.FuelPlan is { } previousFuel)
        previousFuel.NeedsRefresh = true;
      RouteStopTracker.Update(plan, load, null, DateTime.UtcNow);
      await using var transaction = await publication.BeginAsync(work, ct);
      await profiles.RequireCurrentAsync(work.TruckId, observedProfile, ct);
      await profiles.SaveAsync(plan.TruckId, request.Profile, ct);
      await store.SaveBuiltAsync(entity, plan, hash, ct);
      await publication.CommitAsync(
        transaction,
        plan.TruckId,
        ct,
        () =>
        {
          profiles.Invalidate(plan.TruckId);
          store.Invalidate(dispatchId, load.ExecutionLegId);
        }
      );
      return plan;
    }
    finally
    {
      gate.Release();
    }
  }
}
