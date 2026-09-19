using Application.Caching;
using Application.Features.Dispatch.Queries;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Services.Routes;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class TruckFuelPlans(ITruckFuelPlanStore store, ReadCache reads, FuelPlanMemory memory,
  ISender mediator, Application.Features.Dispatch.Interfaces.IDispatchBoardReader dispatchBoard, IOptions<FuelRegionOptions> options)
{
  public Task<TruckFuelPlanSnapshot?> ReadAsync(Guid truckId, CancellationToken ct) =>
    reads.GetAsync($"truck-fuel:{truckId}", "summary", () => store.ReadAsync(truckId, false, ct), TimeSpan.FromSeconds(30));

  public Task<TruckFuelPlanSnapshot?> ReadCheckedAsync(Guid truckId, CancellationToken ct) => store.ReadAsync(truckId, true, ct);
  public Task<TruckFuelPlanSnapshot?> ReadUncachedAsync(Guid truckId, CancellationToken ct) => store.ReadAsync(truckId, false, ct);
  public void Invalidate(Guid truckId) => reads.Invalidate($"truck-fuel:{truckId}");

  public async Task<bool> SaveAsync(TruckFuelPlanSnapshot snapshot, CancellationToken ct)
  {
    var saved = await store.SaveAsync(snapshot, ct);
    reads.Invalidate($"truck-fuel:{snapshot.TruckId}");
    return saved;
  }

  public async Task<bool> ReplaceAsync(TruckFuelPlanSnapshot snapshot, DateTime? expectedCalculatedAt, CancellationToken ct)
  {
    var saved = await store.ReplaceAsync(snapshot, expectedCalculatedAt, ct);
    Invalidate(snapshot.TruckId);
    return saved;
  }

  public async Task ApplyAsync(RoutePlanningState? state, CancellationToken ct)
  {
    if (state?.Plan is not { } plan || plan.Tracking.AllStopsPassed) return;
    var saved = await ReadAsync(plan.TruckId, ct);
    if (saved is null) return;
    var loads = (await dispatchBoard.ReadAsync(new(TruckId: plan.TruckId,
      IncludeHos: false, IncludeFinancials: false, IncludeEta: false, IncludeOverdue: true), ct)).Items.FirstOrDefault()?.Dispatches;
    var index = saved.Stops.ToList().FindIndex(x => x.DispatchId == plan.DispatchId && x.Stop.Id == plan.Tracking.NextStopId);
    var geometry = index >= 0 && loads is not null && FuelPlanProjection.AssignmentsMatch(saved.Plan, plan.DispatchId, loads)
      && FuelPlanProjection.RemainingStopsMatch(saved.Stops, plan.DispatchId, plan.Tracking.NextStopId, loads)
      ? await memory.LegAsync(saved, index, () => ReadCheckedAsync(plan.TruckId, ct), ct) : null;
    plan.FuelPlan = FuelPlanProjection.Project(saved, state, loads ?? [], geometry, DateTime.UtcNow);
    if (saved.Plan.ArrivalPolicy?.PolicySignature != options.Value.Signature)
    { plan.FuelPlan.NeedsRefresh = true; plan.FuelPlan.ScheduleImpact = null; plan.FuelPlan.RefreshReasons.Add("Fuel arrival policy changed. Recalculate fuel."); }
    if (!plan.FuelPlan.NeedsRefresh)
    {
      var date = FuelPricingDate.FromUtc(DateTime.UtcNow);
      var key = $"fuel-prices:{date}:{reads.Generation("fuel")}:{PlanningSettingsService.Signature(state.Profile)}";
      var signature = await memory.PricesAsync(key, async () =>
      {
        var response = await mediator.Send(new GetFuelStationsQuery(date), ct);
        return response.Success && response.Response is { } stations
          ? FuelPriceSignature.From(FuelRegionGrid.Prices(stations, state.Profile, date)) : null;
      }, ct);
      if (signature != saved.Plan.PriceSignature)
      { plan.FuelPlan.NeedsRefresh = true; plan.FuelPlan.ScheduleImpact = null; plan.FuelPlan.RefreshReasons.Add("Fuel prices changed or could not be verified. Recalculate fuel."); }
    }
  }
}
