using System.Text.Json;
using Application.Caching;
using Application.Diagnostics;
using Application.Features.Eta.Interfaces;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.FuelPlanning;

// Publishing a calculated fuel plan: stamping it with the work it was
// calculated for, confirming that work is still current, and writing it
// only if nothing it depends on has moved in the meantime.
public sealed partial class FuelPlanningService
{
  // Opens the publication for the captured work and confirms, inside it, that
  // every input the result was calculated from is still current: the truck
  // itinerary, the saved roads used, the truck profile and the telemetry
  // observation. Any mismatch throws and nothing is written. A provider may
  // be consulted only before the transaction opens, never inside it.
  private async Task<IDbContextTransaction> BeginVerifiedPublicationAsync(
    RoutePlanningState state,
    FuelWorkInputs captured,
    RoutePlan plan,
    TruckRouteProfile profile,
    IReadOnlyCollection<SavedRoadVersion> savedRoads,
    IReadOnlyCollection<DeadheadHistoryBatch> history,
    CancellationToken ct
  )
  {
    var stamp = FuelObservationStamp.Capture(state);
    RequireSameTelemetry(stamp, await plans.GetAsync(captured.Root(plan), ct));
    var transaction = await publication.BeginAsync(
      captured.Itinerary,
      history,
      ct
    );
    try
    {
      await roads.RequireCurrentAsync(
        [
          .. savedRoads,
          state.SavedRoad
            ?? throw new InvalidOperationException(
              "Fuel publication requires the captured current road."
            ),
        ],
        ct
      );
      await profiles.RequireCurrentAsync(plan.TruckId, state.Profile, ct);
      RequireSameTelemetry(
        stamp,
        await plans.GetAsync(
          captured.Root(plan),
          ct,
          PlannedRouteTelemetry.WithoutProviderWait
        )
      );
      await profiles.SaveAsync(plan.TruckId, profile, ct);
      return transaction;
    }
    catch
    {
      await transaction.DisposeAsync();
      throw;
    }
  }

  private async Task<FuelPlan> CommitAsync(
    FuelPlan fuel,
    IReadOnlyList<FuelCandidate> purchases,
    TruckRoute baseline,
    IReadOnlyList<FuelItineraryStop> itinerary,
    List<Guid> ids,
    Dictionary<Guid, string> signatures,
    string assignmentSignature,
    IReadOnlyDictionary<Guid, string> assignmentSignatures,
    IReadOnlyCollection<DeadheadHistoryBatch> history,
    IReadOnlyCollection<SavedRoadVersion> savedRoads,
    FuelWorkInputs captured,
    RoutePlanningState state,
    TruckRouteProfile profile,
    bool manual,
    string priceSignature,
    DateOnly today,
    DateTime? expectedCalculatedAt,
    CancellationToken ct
  )
  {
    var plan = state.Plan!;
    fuel.TruckId = plan.TruckId;
    fuel.ExecutionLegId = plan.ExecutionLegId;
    fuel.AssignmentRevision = plan.AssignmentRevision;
    fuel.StartProgressMiles = state.Progress!.ProgressMiles!.Value;
    fuel.DispatchIds = ids;
    fuel.DispatchSignatures = signatures;
    fuel.AssignmentSignature = assignmentSignature;
    if (fuel.ArrivalPolicy?.NextDispatchId is { } nextDispatchId)
    {
      if (
        !assignmentSignatures.TryGetValue(nextDispatchId, out var nextSignature)
      )
        throw new RoutePlanningException(
          "The pickup after the fuel horizon changed during calculation."
        );
      fuel.DispatchSignatures[nextDispatchId] = nextSignature;
    }
    fuel.ProfileSignature = JsonSerializer.Serialize(
      profile,
      RoutingJson.Options
    );
    fuel.PricingDate = today;
    fuel.PriceSignature = priceSignature;
    fuel.ManualStartingFuel = manual;
    fuel.FuelObservedAt = state.FuelUpdatedAt;
    if (manual)
      fuel.Notes.Add("Starting fuel was entered manually.");
    fuel.Notes.Add(
      "Cost comparison excludes toll differences. Fuel readings and schedule forecasts are estimates."
    );
    double accessMiles = fuel.StartAccessMiles;
    for (var i = 0; i < fuel.Stops.Count; i++)
    {
      var candidate = purchases[i];
      var owner = itinerary[candidate.LegIndex];
      fuel.Stops[i].VisitKey = candidate.VisitKey;
      fuel.Stops[i].DispatchId = owner.DispatchId;
      fuel.Stops[i].BeforeStopId = owner.Stop.Id;
      fuel.Stops[i].CurrentRouteMile = null;
      fuel.Stops[i].CashUsdPerGallon = candidate.PriceUsd;
      fuel.Stops[i].EconomicUsdPerGallon = candidate.EconomicPriceUsd;
      fuel.Stops[i].RouteMilesAhead = candidate.AlongMiles;
      fuel.Stops[i].MilesAhead =
        candidate.AlongMiles + accessMiles + candidate.ExtraInMiles;
      accessMiles += candidate.ExtraInMiles + candidate.ExtraOutMiles;
    }
    fuel.StopArrivals = FuelStopArrivals.Calculate(fuel, itinerary, profile);
    var latest = await inputs.ReadFreshAsync(plan.TruckId, ct);
    var latestLoads = latest.Select(plan);
    if (
      latest.Itinerary.InputSignature != captured.Itinerary.InputSignature
      || !FuelPlanProjection.AssignmentsMatch(
        fuel,
        plan.DispatchId,
        latestLoads
      )
      || !FuelPlanProjection.RemainingStopsMatch(
        itinerary,
        plan.DispatchId,
        plan.Tracking.NextStopId,
        latestLoads
      )
    )
      throw new RoutePlanningException(
        "Assignments changed during fuel calculation."
      );
    await using var transaction = await BeginVerifiedPublicationAsync(
      state,
      captured,
      plan,
      profile,
      savedRoads,
      history,
      ct
    );
    SavedRoadVersion[] dependencies =
    [
      .. savedRoads,
      state.SavedRoad
        ?? throw new InvalidOperationException(
          "Fuel publication requires the captured current road."
        ),
    ];
    await routeStore.StoreFuelAsync(
      plan.DispatchId,
      fuel,
      ct,
      plan.ExecutionLegId
    );
    if (
      !await savedPlans.ReplaceAsync(
        new(
          plan.TruckId,
          plan.DispatchId,
          fuel.CalculatedAt,
          fuel,
          itinerary,
          null
        )
        {
          BaselineRoute = baseline,
          RoadDependencies = FuelRoadDependencies.Capture(dependencies),
          HistoryDependencies = FuelHistoryDependencies.Capture(history),
          RootExecutionLegId = plan.ExecutionLegId,
          AssignmentRevision = plan.AssignmentRevision,
        },
        expectedCalculatedAt,
        ct
      )
    )
      throw new PlanningSettingsConflictException(
        "The fuel plan changed in another session. Reopen it before saving."
      );
    await transaction.CommitAsync(ct);
    profiles.Invalidate(plan.TruckId);
    savedPlans.Invalidate(plan.TruckId);
    routeStore.Invalidate(plan.DispatchId, plan.ExecutionLegId);
    return fuel;
  }
}
