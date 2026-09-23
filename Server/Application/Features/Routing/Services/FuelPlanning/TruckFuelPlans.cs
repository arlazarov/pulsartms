using Application.Caching;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Execution;
using Domain.Models.Fleet;
using Domain.Models.Fuel;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class TruckFuelPlans(
  ITruckFuelPlanStore store,
  ReadCache reads,
  FuelPlanMemory memory,
  IFuelWorkInputsReader inputs,
  ICarrierFuelPrices fuelPrices,
  IOptions<FuelRegionOptions> options,
  IFuelSavedInputsValidation savedInputs,
  FuelIssueRecords issues
)
{
  public Task<TruckFuelPlanSnapshot?> ReadAsync(
    Guid truckId,
    CancellationToken ct
  ) =>
    reads.GetAsync(
      $"truck-fuel:{truckId}",
      "summary",
      () => store.ReadAsync(truckId, false, ct),
      TimeSpan.FromSeconds(30)
    );

  public Task<TruckFuelPlanSnapshot?> ReadCheckedAsync(
    Guid truckId,
    CancellationToken ct
  ) => store.ReadAsync(truckId, true, ct);

  public Task<TruckFuelPlanSnapshot?> ReadUncachedAsync(
    Guid truckId,
    CancellationToken ct
  ) => store.ReadAsync(truckId, false, ct);

  public void Invalidate(Guid truckId)
  {
    reads.Invalidate($"truck-fuel:{truckId}");
  }

  public async Task<bool> SaveAsync(
    TruckFuelPlanSnapshot snapshot,
    CancellationToken ct
  )
  {
    var saved = await store.SaveAsync(snapshot, ct);
    Invalidate(snapshot.TruckId);
    return saved;
  }

  public async Task<bool> ReplaceAsync(
    TruckFuelPlanSnapshot snapshot,
    DateTime? expectedCalculatedAt,
    CancellationToken ct
  )
  {
    var saved = await store.ReplaceAsync(snapshot, expectedCalculatedAt, ct);
    Invalidate(snapshot.TruckId);
    return saved;
  }

  public async Task ApplyAsync(
    RoutePlanningState? state,
    CancellationToken ct,
    TruckItinerarySnapshot? itinerary = null,
    DriverHosClocks? hos = null
  )
  {
    if (state?.Plan is not { } plan || plan.Tracking.AllStopsPassed)
      return;
    if (plan.FuelRecommendations is { AccessProblem: true } access)
    {
      var work =
        itinerary
        ?? (await inputs.ReadDisplayAsync(plan.TruckId, ct))?.Itinerary;
      if (
        work?.InputSignature != access.WorkSignature
        || !access.MatchesTelemetry(state)
        || !await savedInputs.MatchesAsync(access, ct)
      )
        plan.FuelRecommendations = null;
      else
      {
        plan.FuelPlan = null;
        return;
      }
    }
    var saved = await ReadAsync(plan.TruckId, ct);
    if (saved is null)
      return;
    if (!FuelPlanProjection.SameScope(saved, plan))
    {
      plan.FuelPlan = null;
      return;
    }
    var captured = itinerary is null
      ? await inputs.ReadDisplayAsync(plan.TruckId, ct)
      : new FuelWorkInputs(itinerary);
    var loads = captured?.SelectForDisplay(plan);
    var index = saved
      .Stops.ToList()
      .FindIndex(x =>
        x.DispatchId == plan.DispatchId && x.Stop.Id == plan.Tracking.NextStopId
      );
    var geometry =
      index >= 0
      && loads is not null
      && FuelPlanProjection.AssignmentsMatch(saved.Plan, plan.DispatchId, loads)
      && FuelPlanProjection.RemainingStopsMatch(
        saved.Stops,
        plan.DispatchId,
        plan.Tracking.NextStopId,
        loads
      )
        ? await memory.LegAsync(
          saved,
          index,
          () => ReadCheckedAsync(plan.TruckId, ct),
          ct
        )
        : null;
    plan.FuelPlan = FuelPlanProjection.Project(
      saved,
      state,
      loads ?? [],
      geometry,
      DateTime.UtcNow
    );
    if (!await savedInputs.MatchesAsync(saved, plan, ct))
    {
      plan.FuelPlan.NeedsRefresh = true;
      plan.FuelPlan.ScheduleImpact = null;
      plan.FuelPlan.RefreshReasons.Add(
        "Saved fuel roads or history changed or could not be verified. Recalculate fuel."
      );
      plan.FuelPlan.StopArrivals = [];
    }
    if (saved.Plan.ArrivalPolicy?.PolicySignature != options.Value.Signature)
    {
      plan.FuelPlan.NeedsRefresh = true;
      plan.FuelPlan.ScheduleImpact = null;
      plan.FuelPlan.RefreshReasons.Add(
        "Fuel arrival policy changed. Recalculate fuel."
      );
    }
    if (!plan.FuelPlan.NeedsRefresh)
    {
      var date = FuelPricingDate.FromUtc(DateTime.UtcNow);
      var key =
        $"fuel-prices:{date}:{reads.Generation("fuel")}:{PlanningSettingsService.Signature(state.Profile)}";
      var signature = await memory.PricesAsync(
        key,
        async () =>
        {
          return await fuelPrices.ReadAsync(date, ct) is { } stations
            ? FuelPriceSignature.From(
              FuelRegionGrid.Prices(stations, state.Profile, date)
            )
            : null;
        },
        ct
      );
      if (signature != saved.Plan.PriceSignature)
      {
        // Only a change that could move a purchase by what an extra stop
        // must save sends the plan back to a search; a smaller one moves
        // the estimate and leaves the stations where they are.
        var quotes = await memory.QuotesAsync(
          key,
          async () =>
            await fuelPrices.ReadAsync(date, ct) is { } stations
              ? FuelPriceMateriality.Quotes(
                FuelRegionGrid.Prices(stations, state.Profile, date)
              )
              : null,
          ct
        );
        FuelPlanStop[] priced = quotes is null
          ? []
          : plan.FuelPlan.Stops.Where(x => x.PriceDate == date).ToArray();
        FuelPriceMateriality.Quote? Today(FuelPlanStop stop) =>
          quotes?.GetValueOrDefault(stop.StationId);
        if (signature is null || quotes is null)
        {
          plan.FuelPlan.NeedsRefresh = true;
          plan.FuelPlan.ScheduleImpact = null;
          plan.FuelPlan.RefreshReasons.Add(
            "Fuel prices changed or could not be verified. Recalculate fuel."
          );
        }
        else if (FuelPriceMateriality.Material(priced, Today))
        {
          plan.FuelPlan.NeedsRefresh = true;
          plan.FuelPlan.ScheduleImpact = null;
          plan.FuelPlan.RefreshReasons.Add(
            "Fuel prices changed enough to reconsider the stations. Recalculate fuel."
          );
        }
        else
          FuelPriceMateriality.Reprice(
            plan.FuelPlan,
            stop => stop.PriceDate == date ? Today(stop) : null
          );
      }
      if (
        !plan.FuelPlan.NeedsRefresh
        && saved.Plan.UsDiscountSignature.Length > 0
      )
      {
        var dates = saved
          .Plan.PriceDates.Append(date)
          .Distinct()
          .Order()
          .ToArray();
        var calendarKey =
          $"fuel-calendar:{string.Join(',', dates)}:{reads.Generation("fuel")}";
        var calendar = await memory.PricesAsync(
          calendarKey,
          async () =>
          {
            var days = new Dictionary<DateOnly, List<FuelStationDto>>();
            foreach (var day in dates)
            {
              if (await fuelPrices.ReadAsync(day, ct) is not { } dayPrices)
                return null;
              days[day] = dayPrices;
            }
            return UsFuelDiscountSignature.Calendar(days);
          },
          ct
        );
        if (calendar != saved.Plan.UsDiscountSignature)
        {
          var quotes = new Dictionary<
            DateOnly,
            IReadOnlyDictionary<Guid, FuelPriceMateriality.Quote>?
          >();
          foreach (var day in plan.FuelPlan.Stops.Select(x => x.PriceDate).Distinct())
            quotes[day] = await memory.QuotesAsync(
              $"fuel-quotes:{day}:{reads.Generation("fuel")}:{PlanningSettingsService.Signature(state.Profile)}",
              async () =>
                await fuelPrices.ReadAsync(day, ct) is { } stations
                  ? FuelPriceMateriality.Quotes(
                    FuelRegionGrid.Prices(stations, state.Profile, day)
                  )
                  : null,
              ct
            );
          FuelPriceMateriality.Quote? OnTheDay(FuelPlanStop stop) =>
            quotes.GetValueOrDefault(stop.PriceDate)
              ?.GetValueOrDefault(stop.StationId);
          if (
            calendar is null
            || FuelPriceMateriality.Material(plan.FuelPlan.Stops, OnTheDay)
          )
          {
            plan.FuelPlan.NeedsRefresh = true;
            plan.FuelPlan.ScheduleImpact = null;
            plan.FuelPlan.RefreshReasons.Add(
              "Arrival-date fuel prices changed or could not be verified."
            );
          }
          else
            FuelPriceMateriality.Reprice(plan.FuelPlan, OnTheDay);
        }
      }
    }
    await issues.ApplyAsync(saved, plan.FuelPlan, hos, ct);
  }
}
