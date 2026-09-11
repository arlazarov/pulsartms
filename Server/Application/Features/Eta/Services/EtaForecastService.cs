using Application.Features.Dispatch.Models;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Routing.Services.Routes;

namespace Application.Features.Eta.Services;

public sealed class EtaForecastService(EtaChainInputsService inputs, IEtaForecastStore store,
  EtaMemory memory, EtaService eta, RoutePlanningService routes)
{
  public async Task PopulateAsync(IReadOnlyCollection<DispatchResponse> dispatches, CancellationToken ct,
    IReadOnlyList<TruckDispatchBoardResponse>? rows = null)
  {
    if (rows is not null)
      foreach (var row in rows) row.CurrentCycle = null;
    var saved = (await store.ReadAsync(dispatches.Select(x => x.Id).ToArray(), ct)).ToDictionary(x => x.DispatchId);
    var groups = rows is not null ? rows.Where(x => x.TruckId.HasValue)
      .Select(x => (TruckId: x.TruckId!.Value, Loads: (IReadOnlyList<DispatchResponse>)x.Dispatches)).ToArray()
      : dispatches.GroupBy(x => x.TruckId ?? x.Stops.Select(s => s.TruckId).FirstOrDefault(id => id.HasValue))
        .Where(x => x.Key.HasValue).Select(x => (TruckId: x.Key!.Value, Loads: (IReadOnlyList<DispatchResponse>)x.ToArray())).ToArray();
    foreach (var group in groups)
    {
      var description = await inputs.DescribeAsync(group.TruckId, ct, rows is null ? null : group.Loads);
      if (description is null) continue;
      var now = DateTime.UtcNow;
      memory.Demand(description.RootDispatchId, description.InputHash, now);
      if (rows is not null)
      {
        var currentCycle = ReadCurrentCycle(saved.GetValueOrDefault(description.RootDispatchId), description, now);
        foreach (var row in rows.Where(x => x.TruckId == group.TruckId)) row.CurrentCycle = currentCycle;
      }
      foreach (var load in group.Loads)
      {
        if (!description.Loads.Any(x => x.Id == load.Id)) continue;
        var snapshot = saved.GetValueOrDefault(load.Id);
        var matches = Matches(snapshot, description);
        if (matches)
        {
          load.Eta = snapshot!.Forecast.ValidUntil > now ? snapshot.Forecast : snapshot.Forecast with { RouteUpdatePending = true };
          if (snapshot.Forecast.ValidUntil > now) continue;
        }
        else load.Eta = new(now, now, [], "ETA unavailable: forecast updating.", []) { RouteUpdatePending = true };
      }
    }
  }

  private static bool Matches(EtaForecastSnapshot? snapshot, EtaChainDescription description) =>
    snapshot is not null && snapshot.TruckId == description.TruckId
      && snapshot.RootDispatchId == description.RootDispatchId && snapshot.InputHash == description.InputHash
      && snapshot.DriverExternalId == description.DriverExternalId;

  private static DriverCycleSnapshot? ReadCurrentCycle(EtaForecastSnapshot? snapshot,
    EtaChainDescription description, DateTime now)
  {
    if (!Matches(snapshot, description) || snapshot!.DispatchId != description.RootDispatchId
      || string.IsNullOrWhiteSpace(description.DriverExternalId)) return null;
    var forecast = snapshot.Forecast;
    if (forecast.CalculatedAt > now || forecast.ValidUntil <= forecast.CalculatedAt
      || forecast.ValidUntil <= now.AddMinutes(-15)
      || forecast.CycleAtCalculation is not { RecapVerified: true, NextRecapMinutes: > 0, NextRecapAt: { } recap } cycle
      || recap <= now || string.IsNullOrWhiteSpace(cycle.HomeTimeZoneId)) return null;
    return new(forecast.CalculatedAt, forecast.ValidUntil, cycle);
  }

  public async Task RefreshAsync(Guid requestedDispatchId, CancellationToken ct)
  {
    var requested = await routes.LoadAsync(requestedDispatchId, ct);
    if (requested.TruckId is not { } truckId) return;
    var description = await inputs.DescribeAsync(truckId, ct);
    if (description is null)
    {
      memory.Forget(requestedDispatchId);
      return;
    }
    if (description.RootDispatchId != requestedDispatchId)
    {
      memory.Forget(requestedDispatchId);
      memory.View(description.RootDispatchId, DateTime.UtcNow);
    }
    var chain = await inputs.PrepareAsync(description, ct);
    var state = await routes.GetAsync(description.Loads[0], ct, cachedTelemetryOnly: true);
    EtaMemory.Entry? published = null;
    var committed = false;
    var changed = false;
    try
    {
      var result = await eta.GetAsync(state, ct, viewed: false, chain);
      if (result is null)
      {
        var now = DateTime.UtcNow;
        result = new(now, now.AddMinutes(2), [], "ETA unavailable: waiting for the current saved route.", []) { RouteUpdatePending = true };
        memory.Results[description.RootDispatchId] = new(description.InputHash, result) { ChainInputHash = description.InputHash };
      }
      if (memory.Results.TryGetValue(description.RootDispatchId, out var entry) && ReferenceEquals(entry.Value, result)) published = entry;
      var current = await inputs.DescribeAsync(truckId, ct);
      if (current?.InputHash != description.InputHash) { changed = true; return; }
      var snapshots = description.Loads.Select(load => new EtaForecastSnapshot(load.Id, truckId,
        description.RootDispatchId, description.InputHash, description.DriverExternalId, Filter(result, load.Id))).ToArray();
      committed = await store.SaveAsync(snapshots, ct);
      if (!committed)
      {
        var saved = (await store.ReadAsync(snapshots.Select(x => x.DispatchId).ToArray(), ct)).ToDictionary(x => x.DispatchId);
        committed = snapshots.All(snapshot => saved.TryGetValue(snapshot.DispatchId, out var existing)
          && existing.RootDispatchId == snapshot.RootDispatchId && existing.TruckId == snapshot.TruckId
          && existing.InputHash == snapshot.InputHash && existing.DriverExternalId == snapshot.DriverExternalId
          && existing.Forecast.CalculatedAt == snapshot.Forecast.CalculatedAt && existing.Forecast.ValidUntil == snapshot.Forecast.ValidUntil);
      }
    }
    finally
    {
      if (!committed && published is not null && memory.RemoveIfCurrent(description.RootDispatchId, published) && changed)
        memory.RequestRefresh();
    }
  }

  public static DispatchEta Filter(DispatchEta forecast, Guid dispatchId)
  {
    var stops = forecast.Stops.Where(x => x.DispatchId == dispatchId).ToArray();
    var reason = stops.Length == 0 ? forecast.PendingDispatches.GetValueOrDefault(dispatchId) ?? forecast.UnavailableReason : null;
    return forecast with { Stops = stops, UnavailableReason = reason,
      RouteUpdatePending = forecast.RouteUpdatePending || reason is not null,
      PendingDispatches = new Dictionary<Guid, string>() };
  }
}
