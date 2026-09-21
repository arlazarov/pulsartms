using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Execution.Models;
using Application.Features.Execution.Queries;
using Application.Features.Execution.Services;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Microsoft.Extensions.Options;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

// Reading a saved plan: what it needs before it is shown, how far along it
// the truck is, and whether its fuel plan can still be trusted.
public sealed partial class RoutePlanningService
{
  public async Task<RoutePlanningState> GetAsync(
    Guid dispatchId,
    CancellationToken ct,
    bool cachedTelemetryOnly = false
  ) => await GetAsync(await LoadAsync(dispatchId, ct), ct, cachedTelemetryOnly);

  public Task<RoutePlanningState> GetAsync(
    DispatchEntity load,
    CancellationToken ct,
    bool cachedTelemetryOnly = false,
    bool displayOnly = false,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    bool metadataOnly = false
  ) =>
    GetAsync(
      RouteWorkProjection.Capture(load),
      ct,
      cachedTelemetryOnly,
      displayOnly,
      knownPlanId,
      knownVersion,
      metadataOnly
    );

  // withoutProviderWait keeps the GPS eligibility rule and only limits the
  // telemetry source to the latest known observation, so an open database
  // transaction never waits for a provider.
  public async Task<RoutePlanningState> GetAsync(
    RouteWorkSnapshot load,
    CancellationToken ct,
    bool cachedTelemetryOnly = false,
    bool displayOnly = false,
    Guid? knownPlanId = null,
    int? knownVersion = null,
    bool metadataOnly = false,
    bool withoutProviderWait = false
  )
  {
    var dispatchId = load.Id;
    var profile = await ProfileAsync(load.TruckId!.Value, ct);
    var snapshot = displayOnly
      ? await displays.GetAsync(
        dispatchId,
        () => store.ReadUncachedAsync(dispatchId, ct, load.ExecutionLegId),
        ct,
        load.ExecutionLegId
      )
      : null;
    var saved = displayOnly
      ? snapshot?.Metadata
      : await store.ReadAsync(dispatchId, ct, load.ExecutionLegId);
    var plan = snapshot is not null
      ? metadataOnly
        ? snapshot.ReadMetadata()
        : snapshot.ReadPlan(knownPlanId, knownVersion)
      : saved is null
        ? null
        : JsonSerializer.Deserialize<RoutePlan>(
          saved.PlanJson,
          RoutingJson.Options
        );
    if (plan is not null)
    {
      plan.InputsChanged =
        !RoutePlanInputs.Matches(saved!, load, profile)
        || plan.TruckId != load.TruckId;
      plan.Profile = profile;
      if (!metadataOnly && !plan.GeometryOmitted && !plan.InputsChanged)
        await AddDisplayReferenceAsync(plan, load, ct);
      if (plan.FromCurrentPosition && plan.Tracking.PassedStopIds.Count == 0)
        plan.OriginalPlannedMiles = Math.Max(
          plan.OriginalPlannedMiles,
          plan.Route.Miles
        );
      if (
        plan.FuelRecommendations is { AccessProblem: true } access
        && (
          plan.InputsChanged
          || access.Version != plan.Version
          || access.CalculatedAt < DateTime.UtcNow.AddMinutes(-5)
        )
      )
        plan.FuelRecommendations = null;
      plan.Stops = plan
        .Stops.Select(stop => EnrichStop(stop, load.Stops))
        .ToList();
      if (plan.ReferenceStops is not null)
        plan.ReferenceStops = plan
          .ReferenceStops.Select(stop => EnrichStop(stop, load.Stops))
          .ToList();
      if (
        plan.FuelRecommendations is { } recommendations
        && recommendations.SettingsSignature
          != PlanningSettingsService.Signature(profile)
      )
        plan.FuelRecommendations = null;
    }
    TruckLocation? truck = null;
    try
    {
      if (PlanningWorkPolicy.CanUseGps(load) || cachedTelemetryOnly)
        truck = await LocationAsync(
          load.TruckId.Value,
          ct,
          cachedTelemetryOnly || withoutProviderWait
        );
    }
    catch (HttpRequestException) { }
    var progress =
      plan is not null
      && (load.ExecutionStatus is not "planned" || plan.FromCurrentPosition)
        ? RouteProgressMeasure.Of(plan, truck, load, snapshot?.Geometry)
        : null;
    if (plan?.FuelPlan is { } fuel)
      FuelPlanFreshness.Judge(
        plan,
        fuel,
        profile,
        progress,
        truck,
        regionOptions.Value.Signature,
        DateTime.UtcNow
      );
    var state = new RoutePlanningState(
      profile,
      plan,
      progress,
      truck?.FuelPercent is { } f ? (double)f : null,
      truck?.FuelUpdatedAt,
      routing.IsConfigured
    )
    {
      RouteChoiceRevision = load.RouteChoiceRevision,
      SavedRoad =
        !displayOnly && saved is not null && plan is not null
          ? SavedRoadVersion.Plan(saved, plan)
          : null,
    };
    if (
      plan?.FuelRecommendations is { AccessProblem: true } accessResult
      && !accessResult.MatchesTelemetry(state)
    )
      plan.FuelRecommendations = null;
    if (displayOnly)
      AutomaticPlanningService.ProjectRecommendations(
        state,
        snapshot?.Geometry
      );
    return state;
  }

  private static PlanStop EnrichStop(
    PlanStop stop,
    IEnumerable<RouteWorkStop> stops
  )
  {
    var source = stops.FirstOrDefault(x => x.Id == stop.Id);
    return source is null
      ? stop
      : stop with
      {
        Name = source.Name,
        Job = source.Job,
        StateAfter = source.StateAfter,
        ScheduledDate = source.ScheduledDate,
        ScheduledTime = source.ScheduledTime,
        ScheduledDate2 = source.ScheduledDate2,
        ScheduledTime2 = source.ScheduledTime2,
        AppointmentTimeZoneId = source.AppointmentTimeZoneId,
        Commodity = source.Commodity,
        Notes = source.Notes,
      };
  }
}
