using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed partial class FuelPlanningService
{
  private async Task<FuelRecommendations> ReportFuelAccessAsync(
    RoutePlanningState state,
    FuelWorkInputs captured,
    IReadOnlyList<FuelCandidate> candidates,
    double gallons,
    double startAccessMiles,
    TruckRouteProfile profile,
    IReadOnlyCollection<DeadheadHistoryBatch> history,
    IReadOnlyCollection<SavedRoadVersion> savedRoads,
    CancellationToken ct
  )
  {
    var nearest = candidates
      .Where(x => x.AlongMiles >= 0)
      .MinBy(x => x.AlongMiles + x.ExtraInMiles);
    var distance =
      startAccessMiles
      + (nearest?.AlongMiles ?? 0)
      + (nearest?.ExtraInMiles ?? 0);
    var needed = distance / profile.Mpg!.Value;
    var missing = Math.Max(0, needed - gallons);
    var warning =
      nearest is null
        ? "No complete fuel plan was found, and no station on this route could be reached."
      : missing > 0
        ? FormattableString.Invariant(
          $"Cannot reach {nearest.Station.Name}: estimated {distance:N1} mi; "
        )
          + FormattableString.Invariant(
            $"{missing:N1} US gal short with {gallons:N1} US gal on board. Refuel before driving to this station."
          )
      : $"{nearest.Station.Name} is reachable, but no complete fuel plan meets the remaining route and arrival requirements.";
    var plan = state.Plan!;
    var recommendations = new FuelRecommendations
    {
      AccessProblem = true,
      Version = plan.Version,
      CalculatedAt = DateTime.UtcNow,
      SettingsSignature = PlanningSettingsService.Signature(profile),
      Message = warning,
      Observation = FuelObservationStamp.Capture(state),
      WorkSignature = captured.Itinerary.InputSignature,
      RoadDependencies = [.. savedRoads, state.SavedRoad!],
      HistoryDependencies = FuelHistoryDependencies.Capture(history),
      Stations = nearest is null
        ? []
        :
        [
          new()
          {
            StationId = nearest.Station.StationId,
            Name = nearest.Station.Name,
            Address = nearest.Station.Address,
            Point = nearest.Station.Point,
            RouteMile =
              state.Progress!.ProgressMiles!.Value + nearest.AlongMiles,
            MilesAhead = distance,
            YourPrice = (decimal)nearest.Station.YourPrice,
            Currency = nearest.Station.Currency,
            Unit = nearest.Station.Unit,
            Warning = warning,
            ShortfallGallons = missing,
            ArrivalGallons = gallons - needed,
            PreferredReserveGallons = profile.ReserveGallons,
          },
        ],
    };
    await using var transaction = await BeginVerifiedPublicationAsync(
      state,
      captured,
      plan,
      profile,
      savedRoads,
      history,
      ct
    );
    var entity =
      await routeStore.ReadForUpdateAsync(
        plan.DispatchId,
        ct,
        plan.ExecutionLegId
      ) ?? throw new RoutePlanningException("Route not found.");
    var current = SavedRouteReader.Plan(entity.PlanJson)!;
    current.FuelRecommendations = recommendations;
    current.FuelPlan = null;
    await routeStore.SaveAsync(entity, current, ct);
    await transaction.CommitAsync(ct);
    profiles.Invalidate(plan.TruckId);
    routeStore.Invalidate(plan.DispatchId, plan.ExecutionLegId);
    return recommendations;
  }

  // The provider may be consulted only before the publication transaction;
  // inside it the same comparison uses the latest known observation.
  private static void RequireSameTelemetry(
    FuelObservationStamp captured,
    RoutePlanningState latest
  )
  {
    if (!captured.Matches(latest))
      throw new RoutePlanningException(
        "Truck telemetry changed during fuel calculation. Recalculate."
      );
  }
}
