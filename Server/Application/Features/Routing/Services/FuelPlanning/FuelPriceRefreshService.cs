using System.Text.Json;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Fuel;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelPriceRefreshService(
  ITruckFuelPlanStore store,
  IFuelWorkInputsReader inputs,
  ISender sender,
  ICarrierFuelPrices carrierPrices,
  TimeProvider clock,
  IFuelSavedInputsValidation savedInputs
)
{
  public async Task RefreshAsync(
    AutomaticPlanningResult current,
    CancellationToken ct
  )
  {
    if (
      current.DispatchId is not { } dispatchId
      || current.State?.Plan
        is not { InputsChanged: false, Tracking.AllStopsPassed: false }
    )
      return;
    var saved = await store.ReadAsync(current.TruckId, false, ct);
    if (
      saved is null
      || saved.Plan.ManuallyEdited
      || saved.Plan.ManualStartingFuel
    )
      return;
    if (
      saved.RootDispatchId != dispatchId
      || saved.RootExecutionLegId != current.ExecutionLegId
      || saved.AssignmentRevision != current.AssignmentRevision
      || saved.Plan.SelectionVersion < FuelOptimizer.SelectionVersion
      || saved.Plan.ProfileSignature
        != JsonSerializer.Serialize(current.State.Profile, RoutingJson.Options)
    )
    {
      await RecalculateAsync(saved, current, dispatchId, ct);
      return;
    }
    var captured = await inputs.ReadDisplayAsync(current.TruckId, ct);
    if (captured is null)
      return;
    var loads = captured.SelectForDisplay(
      new()
      {
        TruckId = current.TruckId,
        DispatchId = dispatchId,
        ExecutionLegId = current.ExecutionLegId,
        AssignmentRevision = current.AssignmentRevision,
      }
    );
    if (!FuelPlanProjection.AssignmentsMatch(saved.Plan, dispatchId, loads))
    {
      await RecalculateAsync(saved, current, dispatchId, ct);
      return;
    }
    if (!await savedInputs.MatchesAsync(saved, current.State.Plan, ct))
    {
      await RecalculateAsync(saved, current, dispatchId, ct);
      return;
    }
    var today = FuelPricingDate.FromUtc(clock.GetUtcNow().UtcDateTime);
    // A plan priced on an earlier day is recalculated because of that alone.
    // Without this the refresh and the reader disagree about what "stale"
    // means: the reader calls a plan stale when its PricingDate is not
    // today, while this decided only by whether the discounts had changed.
    // Unchanged discounts therefore left a plan priced yesterday forever -
    // permanently marked stale by one half of the system and never repaired
    // by the other. Truck 11007 sat like that for a day.
    if (saved.Plan.PricingDate != today)
    {
      await RecalculateAsync(saved, current, dispatchId, ct);
      return;
    }
    var days = new Dictionary<DateOnly, List<FuelStationDto>>();
    foreach (var date in saved.Plan.PriceDates.Append(today).Distinct())
    {
      if (await carrierPrices.ReadAsync(date, ct) is not { } dayPrices)
        return;
      days[date] = dayPrices;
    }
    // A stop the station list no longer offers is a stop the driver cannot
    // use. The list already leaves out stations the place provider reports
    // as closed, so a travel stop that shuts while a plan points at it is
    // caught here - by the plan being redone, not by the stop quietly
    // disappearing and leaving the driver with nowhere to fuel.
    var offered = days.Values.SelectMany(x => x).Select(x => x.Id).ToHashSet();
    if (saved.Plan.Stops.Any(x => !offered.Contains(x.StationId)))
    {
      await RecalculateAsync(saved, current, dispatchId, ct);
      return;
    }
    var signature = UsFuelDiscountSignature.Calendar(days);
    if (saved.Plan.UsDiscountSignature == signature)
      return;
    await RecalculateAsync(saved, current, dispatchId, ct);
  }

  private async Task RecalculateAsync(
    TruckFuelPlanSnapshot saved,
    AutomaticPlanningResult current,
    Guid dispatchId,
    CancellationToken ct
  )
  {
    // The saved revision is checked again inside the per-truck calculation
    // gate. Failed or cancelled calculations keep it unchanged for retry.
    var result = await sender.Send(
      new RecalculateFuelPlanCommand(
        dispatchId,
        current.ExecutionLegId,
        current.AssignmentRevision
      )
      {
        AutomaticRefreshRevision = saved.CalculatedAt,
      },
      ct
    );
    if (!result.Success)
      throw new RoutePlanningException(
        result.Errors?.FirstOrDefault() ?? "Fuel preparation will retry."
      );
  }
}
