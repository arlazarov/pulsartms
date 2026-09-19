using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Algorithms;
using Domain.Entities.Dispatch;
using Load = Domain.Entities.Dispatch.Dispatch;
using Application.Caching;

namespace Application.Features.Routing.Services.Deadheads;

public sealed class DeadheadService(IAppDbContext db, IRoutingProvider routing, RoutePlanningService plans, DispatchRates financials,
  IDeadheadHistoryReader historyReader, ReadCache reads, ProcessGates processGates)
{
  private readonly KeyedGates gates = processGates.For<DeadheadService>();

  public Task<IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>> ReadHistoryAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
    historyReader.ReadAsync(ids, ct);

  public async Task<TruckRoute?> ReadRouteAsync(Guid previousId, Load current, TruckRouteProfile profile, CancellationToken ct)
  {
    var routes = await ReadRoutesAsync(current.TruckId, [current], profile, ct);
    return routes.TryGetValue(current.Id, out var route) && route.PreviousId == previousId ? route.Route : null;
  }

  public async Task<Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>> ReadRoutesAsync(
    Guid? truckId, IReadOnlyCollection<Load> loads, TruckRouteProfile profile, CancellationToken ct)
  {
    var result = new Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>();
    if (truckId is null || loads.Count == 0) return result;
    var history = await historyReader.ReadAsync(loads.Select(x => x.Id).ToArray(), ct);
    return await ReadRoutesAsync(loads, profile, history, ct);
  }

  public async Task<Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>> ReadRoutesAsync(
    IReadOnlyCollection<Load> loads, TruckRouteProfile profile, IReadOnlyDictionary<Guid, DeadheadHistorySnapshot> history, CancellationToken ct)
  {
    var result = new Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>();
    var ids = loads.Select(x => x.Id).ToArray();
    var saved = await db.DispatchDeadheads.AsNoTracking().Where(x => ids.Contains(x.DispatchId)).ToDictionaryAsync(x => x.DispatchId, ct);
    foreach (var load in loads)
    {
      var pair = DeadheadConnection.Find(history.GetValueOrDefault(load.Id));
      var route = pair?.ReadRoute(saved.GetValueOrDefault(load.Id), profile);
      if (route is not null) result[load.Id] = (pair!.Previous.Id, route);
    }
    return result;
  }

  public async Task ReadAsync(IReadOnlyCollection<DispatchResponse> items, CancellationToken ct)
  {
    if (items.Count == 0) return;
    var ids = items.Select(x => x.Id).ToArray();
    var history = await historyReader.ReadAsync(ids, ct);
    var saved = await db.DispatchDeadheads.AsNoTracking().Where(x => ids.Contains(x.DispatchId)).ToDictionaryAsync(x => x.DispatchId, ct);
    var rates = await db.DispatchRates.AsNoTracking().Where(x => ids.Contains(x.DispatchId)).ToDictionaryAsync(x => x.DispatchId, ct);
    var profiles = new Dictionary<Guid, TruckRouteProfile>();
    foreach (var item in items)
    {
      item.EmptyMiles = null;
      item.EmptyMilesStatus = "unavailable";
      item.LoadedRatePerMile = DispatchRates.PerMile(item.Price, item.LoadedMiles);
      item.TotalRatePerMile = null;
      var snapshot = history.GetValueOrDefault(item.Id);
      var load = snapshot?.Current;
      var pair = DeadheadConnection.Find(snapshot);
      if (pair is null) continue;
      var truck = load!.TruckId!.Value;
      if (!profiles.TryGetValue(truck, out var profile)) profiles[truck] = profile = await plans.ProfileAsync(truck, ct);
      item.EmptyMilesStatus = "pending";
      if (saved.TryGetValue(item.Id, out var entry) && entry.InputHash == pair.Signature(profile))
      {
        item.EmptyMiles = entry.Miles;
        item.EmptyMilesStatus = entry.Miles.HasValue ? "ready" : "unavailable";
        if (rates.TryGetValue(item.Id, out var rate) && DispatchRates.Matches(rate, load, entry.Miles, entry.InputHash)
          && rate.Price == item.Price && rate.LoadedMiles == item.LoadedMiles && rate.Currency == item.Currency)
        {
          item.LoadedRatePerMile = rate.LoadedRatePerMile;
          item.TotalRatePerMile = rate.TotalRatePerMile;
        }
        else if (entry.Miles is >= 0 && item.LoadedMiles is > 0)
          item.TotalRatePerMile = DispatchRates.PerMile(item.Price, item.LoadedMiles + entry.Miles);
      }
    }
  }

  public async Task EnsureAsync(Load load, TruckRouteProfile profile, CancellationToken ct)
  {
    if (load.Status is not ("assigned" or "in_transit" or "unassigned")) return;
    if (load.TruckId is null)
    {
      await financials.SaveAsync(load, null, "", ct);
      return;
    }
    var history = await historyReader.ReadAsync([load.Id], ct);
    var latestLoad = history.GetValueOrDefault(load.Id)?.Current;
    if (latestLoad is null || latestLoad.Status is not ("assigned" or "in_transit" or "unassigned")) return;
    load = latestLoad;
    var pair = DeadheadConnection.Find(history.GetValueOrDefault(load.Id));
    var hash = pair is not null && profile.Validate() is null ? pair.Signature(profile) : "";
    var gate = gates.For(load.TruckId ?? load.Id);
    await GateWait.WaitAsync(gate, "Deadhead", ct);
    try
    {
      var saved = await db.DispatchDeadheads.SingleOrDefaultAsync(x => x.DispatchId == load.Id, ct);
      await financials.SaveAsync(load, hash.Length > 0 && saved?.InputHash == hash ? saved.Miles : null, hash, ct);
      if (pair is null || hash.Length == 0 || !routing.IsConfigured || load.Status == "unassigned") return;
      var sameInputs = saved?.InputHash == hash;
      if (saved is not null && sameInputs && (saved.RetryAfter > DateTime.UtcNow
        || saved.Miles.HasValue && pair.ReadRoute(saved, profile) is not null)) return;
      if (saved is null)
      {
        saved = new() { Id = Guid.NewGuid(), DispatchId = load.Id };
        db.DispatchDeadheads.Add(saved);
      }
      saved.InputHash = hash;
      saved.PreviousDispatchId = pair.Previous.Id;
      // Missing display geometry must not erase valid financial mileage for the same inputs.
      if (!sameInputs) saved.Miles = null;
      saved.RouteJson = null;
      if (!sameInputs) saved.CalculatedAt = null;
      // Persist the retry budget before making any billable provider requests.
      saved.ErrorMessage = null;
      saved.RetryAfter = DateTime.UtcNow.AddMinutes(5);
      try { await db.SaveChangesAsync(ct); reads.Invalidate($"chain:{load.Id}"); }
      catch (DbUpdateConcurrencyException) { db.Entry(saved).State = EntityState.Detached; return; }
      try
      {
        var from = await StopLocation.ResolveAsync(pair.From, routing, ct);
        var to = await StopLocation.ResolveAsync(pair.To, routing, ct);
        var route = await routing.CalculateAsync([from, to], profile, ct);
        if (!SavedRouteGeometry.Complete(route, 1))
          throw new RoutePlanningException("Empty route geometry is incomplete.", saved.RetryAfter);
        if (!RouteAnchoring.Matches(route, [from, to]))
          throw new RoutePlanningException("The empty route does not reach the confirmed stops.", saved.RetryAfter);
        // Recheck assignments and stops after the external request.
        var latest = await historyReader.ReadAsync([load.Id], ct);
        var currentSnapshot = latest.GetValueOrDefault(load.Id);
        var current = currentSnapshot?.Current;
        if (current is null || DeadheadConnection.Find(currentSnapshot)?.Signature(profile) != hash) return;
        saved.Miles = (decimal)route.Miles;
        saved.RouteJson = RoutePlanStorage.Serialize(route);
        saved.CalculatedAt = DateTime.UtcNow;
        saved.RetryAfter = DateTime.MinValue;
        saved.ErrorMessage = null;
        await db.SaveChangesAsync(ct);
        reads.Invalidate($"chain:{load.Id}");
        await financials.SaveAsync(current, saved.Miles, hash, ct);
      }
      catch (RoutePlanningException ex)
      {
        saved.ErrorMessage = ex.Message;
        saved.RetryAfter = ex.RetryAfter;
        await db.SaveChangesAsync(ct);
        reads.Invalidate($"chain:{load.Id}");
        throw;
      }
    }
    finally { gate.Release(); }
  }
}
