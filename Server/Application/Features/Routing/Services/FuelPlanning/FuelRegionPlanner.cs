using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Options;
using Application.Features.Routing.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Services.Deadheads;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelRegionPlanner(Application.Features.Dispatch.Interfaces.IDispatchBoardReader board, IOptions<FuelRegionOptions> options,
  RoutePlanningService plans, DeadheadService deadheads)
{
  public async Task<FuelArrivalPolicy> BuildAsync(RoutePlan plan, TruckRouteProfile profile,
    IReadOnlyList<PricedFuelStation> prices, double progress, CancellationToken ct, FuelSearchGeometry? searchGeometry = null)
  {
    var config = options.Value;
    var geometry = searchGeometry ?? new FuelSearchGeometry(plan.Route, ct);
    var delivery = geometry.At(geometry.Miles);
    var corridor = prices.Where(x => geometry.Match(x.Station.Point, progress, ct).Away <= 2).ToList();
    var nearby = prices.Where(x => RouteGeometry.Distance(delivery, x.Station.Point) <= config.EscapeSearchMiles).ToList();
    var comparison = corridor.Concat(nearby).DistinctBy(x => x.Station.StationId).Select(x => x.EconomicUsd).Order().ToArray();
    if (comparison.Length == 0) throw new RoutePlanningException("No priced truck fuel stations are available around delivery. Post-delivery fuel access cannot be estimated yet.");
    var reference = comparison[(comparison.Length - 1) / 4];
    var grid = new FuelRegionGrid(prices, config, reference);
    var cells = grid.Along(geometry, progress, ct);
    var local = nearby.Where(x => RouteGeometry.Distance(delivery, x.Station.Point) <= config.LocalSearchMiles).ToList();
    var poor = grid.Cell(delivery).Kind != "good" || local.Count < config.MinimumStations || local.Min(x => x.EconomicUsd) >= reference + config.ExpensivePremiumUsdPerGallon;

    // With an assigned pickup, examine fuel along that direction, not an arbitrary exit.
    var loads = (await board.ReadAsync(new(TruckId: plan.TruckId, IncludeHos: false, IncludeFinancials: false, IncludeEta: false, IncludeOverdue: true), ct))
      .Items.FirstOrDefault()?.Dispatches ?? [];
    var index = loads.FindIndex(x => x.Id == plan.DispatchId);
    var next = index >= 0 ? loads.Skip(index + 1).FirstOrDefault() : null;
    FuelSearchGeometry? onward = null;
    if (next is not null)
    {
      var load = await plans.LoadAsync(next.Id, ct);
      if (load.TruckId != plan.TruckId)
        throw new RoutePlanningException("The next pickup assignment changed. Reload the route before calculating fuel.");
      var pickup = load.Stops.OrderBy(x => x.Sequence).FirstOrDefault()
        ?? throw new RoutePlanningException("The next pickup location is unavailable for fuel planning.");
      var point = FuelHorizon.ConfirmedPoint(pickup, DateTime.UtcNow);
      var saved = await deadheads.ReadRouteAsync(plan.DispatchId, load, profile, ct);
      if (!SavedRouteGeometry.Complete(saved, 1) || !RouteAnchoring.Matches(saved, [delivery, point]))
        throw new RoutePlanningException("A matching saved connection is required to estimate fuel access in the next pickup direction.");
      onward = new(saved!, ct);
    }
    var eligible = onward is null ? nearby : nearby.Where(x => onward.Match(x.Station.Point, ct: ct).Away <= 2
      || RouteGeometry.Distance(onward.At(onward.Miles), x.Station.Point) <= 10).ToList();
    if (eligible.Count == 0) throw new RoutePlanningException("No priced fuel station is available after delivery in the next pickup direction.");
    var nearest = eligible.MinBy(x => RouteGeometry.Distance(delivery, x.Station.Point))!;
    var inexpensive = eligible.Where(x => x.EconomicUsd < reference + config.ExpensivePremiumUsdPerGallon)
      .OrderBy(x => RouteGeometry.Distance(delivery, x.Station.Point));
    var shortlist = inexpensive.Take(config.MaximumRoadChecks - 1).Append(nearest).Concat(eligible.OrderBy(x => RouteGeometry.Distance(delivery, x.Station.Point))).DistinctBy(x => x.Station.StationId)
      .Take(config.MaximumRoadChecks).ToList();
    var estimated = new List<(PricedFuelStation Station, double Miles)>();
    foreach (var station in shortlist)
    {
      ct.ThrowIfCancellationRequested();
      var miles = FuelAccessEstimate.DistanceMiles(RouteGeometry.Distance(delivery, station.Station.Point));
      if (miles / profile.Mpg + profile.ReserveGallons <= profile.TankGallons * profile.FillPercent / 100)
        estimated.Add((station, miles));
    }
    if (estimated.Count == 0) throw new RoutePlanningException("No post-delivery fuel access estimate fits the configured tank and reserve.");
    var exits = poor ? estimated.Where(x => x.Station.EconomicUsd < reference + config.ExpensivePremiumUsdPerGallon).ToList() : [];
    var chosen = (exits.Count > 0 ? exits : estimated).MinBy(x => x.Miles);
    var minimumMiles = Math.Max(config.AfterDeliveryBufferMiles, chosen.Miles);
    var minimum = Math.Ceiling(profile.ReserveGallons) + Math.Ceiling(minimumMiles / profile.Mpg!.Value);
    // Half of physical tank capacity is a hard arrival floor in expensive or
    // poorly served regions. A longer estimated escape can require more.
    if (poor) minimum = Math.Max(minimum, Math.Ceiling(profile.TankGallons!.Value * .5));
    var target = Math.Max(minimum, Math.Floor(profile.TankGallons!.Value * profile.FillPercent / 100));
    foreach (var station in estimated)
    { var cell = grid.Cell(station.Station.Station.Point); if (cells.All(x => x.Id != cell.Id)) cells.Add(cell); }
    return new() { MinimumGallons = minimum, TargetGallons = target,
      ReplacementPriceUsd = chosen.Station.EconomicUsd, PoorArea = poor, EconomicPurchasesOnly = true,
      EscapeStationId = chosen.Station.Station.StationId, EscapeStationName = chosen.Station.Station.Name,
      EscapeMiles = chosen.Miles, NextDispatchId = next?.Id, PolicySignature = config.Signature,
      Regions = cells, Reason = poor
        ? "Arrive with at least half a tank, or the estimated access fuel plus reserve when greater. Station access has not been road-checked."
        : "Fuel retained for estimated post-delivery station access with reserve. Station access has not been road-checked." };
  }
}
