using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Eta.Interfaces;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Ports;
using Domain.Rules.Routing;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed record FuelArrivalInputs(
  FuelArrivalPolicy Policy,
  DeadheadHistoryBatch? History,
  SavedRoadVersion? Road
);

public sealed class FuelRegionPlanner(
  IFuelWorkInputsReader inputs,
  IOptions<FuelRegionOptions> options,
  DeadheadService deadheads,
  IRouteRegionLookup regionLookup
)
{
  public async Task<FuelArrivalInputs> BuildAsync(
    RoutePlan plan,
    TruckRouteProfile profile,
    IReadOnlyList<PricedFuelStation> prices,
    double progress,
    CancellationToken ct,
    FuelSearchGeometry? searchGeometry = null,
    FuelWorkInputs? suppliedInputs = null,
    IReadOnlySet<Guid>? corridorStations = null
  )
  {
    // "build-total" contains every stage below it. The sizes are counted
    // beside the times because a slow filter and a filter run many times look
    // the same on a clock.
    using var building = PerformanceStages.Start("fuel-regions", "build-total");
    PerformanceStages.Count("fuel-regions", "priced-stations", prices.Count);
    var at = Stopwatch.GetTimestamp();
    var config = options.Value;
    var countries = new FuelAccessCountries(regionLookup);
    var geometry = searchGeometry ?? new FuelSearchGeometry(plan.Route, ct);
    var delivery = geometry.At(geometry.Miles);
    at = Mark("setup", at);
    // One Match against the road for every priced station there is.
    var corridor = prices
      .Where(x =>
      {
        if (corridorStations?.Contains(x.Station.StationId) == false)
          return false;
        var match = geometry.Match(x.Station.Point, progress, ct);
        return match.Away <= 2 && countries.Matches(match.Point, x.Station);
      })
      .ToList();
    at = Mark("corridor", at);
    PerformanceStages.Count("fuel-regions", "corridor-kept", corridor.Count);
    var nearby = prices
      .Where(x =>
        RouteGeometry.Distance(delivery, x.Station.Point)
        <= config.EscapeSearchMiles
      )
      .ToList();
    at = Mark("nearby", at);
    PerformanceStages.Count("fuel-regions", "nearby-kept", nearby.Count);
    var destinationPrices = nearby
      .Where(x => countries.Matches(delivery, x.Station))
      .ToList();
    at = Mark("destination-prices", at);
    // With an assigned pickup, examine fuel along that direction, not an
    // arbitrary exit.
    var captured =
      suppliedInputs ?? await inputs.ReadFreshAsync(plan.TruckId, ct);
    // Records nothing when the caller supplied the inputs, which is the edit
    // path's case - a missing row here means it was handed them, not that the
    // read was free.
    if (suppliedInputs is null)
      at = Mark("inputs", at);
    var loads = captured.Select(plan);
    var index = loads.FindIndex(x => x.Id == plan.DispatchId);
    var next = index >= 0 ? loads.Skip(index + 1).FirstOrDefault() : null;
    FuelSearchGeometry? onward = null;
    DeadheadHistoryBatch? history = null;
    SavedRoadVersion? road = null;
    if (next is not null)
    {
      var load = captured.Resolve(next);
      if (load.TruckId != plan.TruckId)
        throw new RoutePlanningException(
          "The next pickup assignment changed. Reload the route before calculating fuel."
        );
      var pickup =
        load.Stops.OrderBy(x => x.Sequence).FirstOrDefault()
        ?? throw new RoutePlanningException(
          "The next pickup location is unavailable for fuel planning."
        );
      var point = FuelHorizon.ConfirmedPoint(pickup, DateTime.UtcNow);
      at = Mark("next-pickup", at);
      var capturedRoute = await deadheads.CaptureRouteAsync(
        plan.DispatchId,
        load,
        profile,
        ct
      );
      at = Mark("next-connection", at);
      history = capturedRoute.History;
      road = capturedRoute.Road;
      var saved = capturedRoute.Route;
      if (
        !SavedRouteGeometry.Complete(saved, 1)
        || !RouteAnchoring.Matches(saved, [delivery, point])
      )
        throw new RoutePlanningException(
          "A matching saved connection is required to estimate fuel access in the next pickup direction."
        );
      onward = new(saved!, ct);
      at = Mark("onward-geometry", at);
    }
    var eligible = onward is null
      ? destinationPrices
      : nearby
        .Where(x =>
        {
          var match = onward.Match(x.Station.Point, ct: ct);
          var pickup = onward.At(onward.Miles);
          return match.Away <= 2 && countries.Matches(match.Point, x.Station)
            || RouteGeometry.Distance(pickup, x.Station.Point) <= 10
              && countries.Matches(pickup, x.Station);
        })
        .ToList();
    at = Mark("eligible", at);
    PerformanceStages.Count("fuel-regions", "eligible-kept", eligible.Count);
    if (eligible.Count == 0)
      return new(
        ReserveWithoutKnownExit(profile, config, next?.Id),
        history,
        road
      );
    var comparison = corridor
      .Concat(eligible)
      .DistinctBy(x => x.Station.StationId)
      .Select(x => x.EconomicUsd)
      .Order()
      .ToArray();
    var reference = comparison[(comparison.Length - 1) / 4];
    at = Mark("comparison", at);
    var grid = new FuelRegionGrid(prices, config, reference);
    at = Mark("grid", at);
    var cells = grid.Along(geometry, progress, ct);
    at = Mark("grid-along", at);
    var local = destinationPrices
      .Where(x =>
        RouteGeometry.Distance(delivery, x.Station.Point)
        <= config.LocalSearchMiles
      )
      .ToList();
    var poor =
      grid.Cell(delivery).Kind != "good"
      || local.Count < config.MinimumStations
      || local.Min(x => x.EconomicUsd)
        >= reference + config.ExpensivePremiumUsdPerGallon;
    var nearest = eligible.MinBy(x =>
      RouteGeometry.Distance(delivery, x.Station.Point)
    )!;
    var inexpensive = eligible
      .Where(x =>
        x.EconomicUsd < reference + config.ExpensivePremiumUsdPerGallon
      )
      .OrderBy(x => RouteGeometry.Distance(delivery, x.Station.Point));
    var shortlist = inexpensive
      .Take(config.MaximumRoadChecks - 1)
      .Append(nearest)
      .Concat(
        eligible.OrderBy(x => RouteGeometry.Distance(delivery, x.Station.Point))
      )
      .DistinctBy(x => x.Station.StationId)
      .Take(config.MaximumRoadChecks)
      .ToList();
    var estimated = new List<(PricedFuelStation Station, double Miles)>();
    foreach (var station in shortlist)
    {
      ct.ThrowIfCancellationRequested();
      var miles = FuelAccessEstimate.DistanceMiles(
        RouteGeometry.Distance(delivery, station.Station.Point)
      );
      if (
        miles / profile.Mpg + profile.ReserveGallons
        <= profile.TankGallons * profile.FillPercent / 100
      )
        estimated.Add((station, miles));
    }
    at = Mark("shortlist-estimate", at);
    PerformanceStages.Count("fuel-regions", "shortlisted", shortlist.Count);
    if (estimated.Count == 0)
      throw new RoutePlanningException(
        "No post-delivery fuel access estimate fits the configured tank and reserve."
      );
    var exits = poor
      ? estimated
        .Where(x =>
          x.Station.EconomicUsd
          < reference + config.ExpensivePremiumUsdPerGallon
        )
        .ToList()
      : [];
    var chosen = (exits.Count > 0 ? exits : estimated).MinBy(x => x.Miles);
    var minimumMiles = Math.Max(config.AfterDeliveryBufferMiles, chosen.Miles);
    var minimum =
      Math.Ceiling(profile.ReserveGallons)
      + Math.Ceiling(minimumMiles / profile.Mpg!.Value);
    // Half of physical tank capacity is a hard arrival floor in expensive or
    // poorly served regions. A longer estimated escape can require more.
    if (poor)
      minimum = Math.Max(
        minimum,
        Math.Ceiling(profile.TankGallons!.Value * .5)
      );
    var target = Math.Max(
      minimum,
      Math.Floor(profile.TankGallons!.Value * profile.FillPercent / 100)
    );
    foreach (var station in estimated)
    {
      var cell = grid.Cell(station.Station.Station.Point);
      if (cells.All(x => x.Id != cell.Id))
        cells.Add(cell);
    }
    return new(
      new()
      {
        MinimumGallons = minimum,
        TargetGallons = target,
        ReplacementPriceUsd = chosen.Station.EconomicUsd,
        PoorArea = poor,
        EconomicPurchasesOnly = true,
        EscapeStationId = chosen.Station.Station.StationId,
        EscapeStationName = chosen.Station.Station.Name,
        EscapeMiles = chosen.Miles,
        NextDispatchId = next?.Id,
        PolicySignature = config.Signature,
        Regions = cells,
        Reason = poor
          ? "Arrive with at least half a tank, or the estimated access fuel plus reserve when greater. Station access has not been road-checked."
          : "Fuel retained for estimated post-delivery station access with reserve. Station access has not been road-checked.",
      },
      history,
      road
    );
  }

  private static FuelArrivalPolicy ReserveWithoutKnownExit(
    TruckRouteProfile profile,
    FuelRegionOptions config,
    Guid? nextDispatchId
  )
  {
    var buffer = Math.Max(
      config.AfterDeliveryBufferMiles,
      config.PoorAreaBufferMiles
    );
    var minimum = Math.Max(
      Math.Ceiling(profile.TankGallons!.Value * .5),
      Math.Ceiling(profile.ReserveGallons)
        + Math.Ceiling(buffer / profile.Mpg!.Value)
    );
    // Missing price coverage does not establish an exit station or a
    // replacement fuel price. Only the conservative arrival floor is known.
    return new()
    {
      MinimumGallons = minimum,
      TargetGallons = minimum,
      PoorArea = true,
      EconomicPurchasesOnly = true,
      NextDispatchId = nextDispatchId,
      PolicySignature = config.Signature,
      Reason =
        "Post-delivery price coverage is unavailable. Keep at least half a tank and the configured buffer plus reserve. No exit station or replacement price is assumed.",
    };
  }

  // Close one stage and open the next. A stage whose branch was not taken
  // records nothing, so a missing row means it did not run.
  private static long Mark(string stage, long since)
  {
    PerformanceStages.Elapsed("fuel-regions", stage, since);
    return Stopwatch.GetTimestamp();
  }
}
