using System.Text.Json;
using Domain.Models.Eta;
using Domain.Models.Routing;

namespace Application.Features.Eta.Services;

// What the service remembers of a forecast and hands back on a read: the
// road it was calculated on, the work it belongs to, and whether a reader
// may still see it.
public sealed partial class EtaService
{
  private static string RouteKey(RoutePlanningState state) =>
    JsonSerializer.Serialize(
      new
      {
        state.Plan?.Id,
        state.Plan?.ExecutionLegId,
        state.Plan?.AssignmentRevision,
        state.Plan?.Version,
        state.Plan?.Tracking.NextStopId,
        state.Plan?.Tracking.PassedStopIds,
        state.Plan?.InputsChanged,
        state.Plan?.Stops,
        state.Progress?.OffRoute,
        state.Progress?.LocationStale,
      }
    );

  // The load, its leg, the assignment and its stops: what a forecast is a
  // forecast of. The road, its version and the truck's place on it are not
  // part of this - they change while the work stays the same.
  private static string WorkKey(RoutePlanningState state) =>
    JsonSerializer.Serialize(
      new
      {
        state.Plan?.TruckId,
        state.Plan?.DispatchId,
        state.Plan?.ExecutionLegId,
        state.Plan?.AssignmentRevision,
        state.Plan?.Stops,
      }
    );

  // A forecast is kept against the road it was calculated on and the work
  // it belongs to, so a later read can tell a newer road for the same work
  // from different work.
  public void Record(
    RoutePlanningState state,
    string signature,
    DispatchEta value,
    string? chainInputHash = null,
    string? driver = null
  )
  {
    if (state.Plan is not { } plan)
      return;
    memory.Publish(
      memory.Scope(plan.DispatchId, plan.ExecutionLegId),
      new(signature, value, RouteKey(state))
      {
        ChainInputHash = chainInputHash,
        WorkKey = WorkKey(state),
        Driver = string.IsNullOrEmpty(driver) ? null : driver,
        PlanId = plan.Id,
        PlanVersion = plan.Version,
      }
    );
  }

  // A forecast that is out of date for the same work is not thrown away:
  // it is returned marked as updating, and the worker is woken to replace
  // it. The Dispatch board already reads saved forecasts this way; the map
  // read the same work as having no forecast at all until a new one landed.
  // One for other work - another load, leg, assignment or set of stops - is
  // never shown in its place.
  public DispatchEta? GetCached(RoutePlanningState state)
  {
    if (state.Plan is not { } plan)
      return null;
    var key = memory.Scope(plan.DispatchId, plan.ExecutionLegId);
    memory.View(key, DateTime.UtcNow);
    if (memory.Results.TryGetValue(key, out var entry))
    {
      // The road key does not name the truck, so the work has to match
      // before a forecast counts as current, not only before it is kept.
      var sameWork = entry.WorkKey is { } work && work == WorkKey(state);
      var current = sameWork && entry.RouteKey == RouteKey(state);
      if (
        current
        && !entry.Superseded
        && entry.Value.ValidUntil > DateTime.UtcNow
      )
        return entry.Value;
      if (sameWork)
      {
        if (current || memory.SupersedeIfCurrent(key, entry))
          memory.RequestRefresh();
        return entry.Value with { RouteUpdatePending = true };
      }
      if (memory.RemoveIfCurrent(key, entry))
        memory.RequestRefresh();
    }
    return null;
  }
}
