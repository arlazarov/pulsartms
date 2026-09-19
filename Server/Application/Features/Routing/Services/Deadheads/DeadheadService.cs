using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Deadheads;

public sealed class DeadheadService(
  IAppDbContext db,
  IRoutingProvider routing,
  RoutePlanningService plans,
  DispatchRates financials,
  DeadheadHistoryService historyReader,
  DeadheadHistoryPublication publication
)
{
  private static readonly KeyedGates Gates = new();

  public Task<
    IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>
  > ReadHistoryAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
    historyReader.ReadAsync(ids, ct);

  public async Task<(
    TruckRoute? Route,
    DeadheadHistoryBatch History,
    SavedRoadVersion Road
  )> CaptureRouteAsync(
    Guid previousId,
    RouteWorkSnapshot current,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var history = await historyReader.ReadLoadedAsync([current], ct);
    var (route, road) = await ReadCapturedRouteAsync(
      previousId,
      current,
      profile,
      history,
      ct
    );
    return (route, new([history[current.Id]]), road);
  }

  internal async Task<(
    TruckRoute? Route,
    SavedRoadVersion Road
  )> ReadCapturedRouteAsync(
    Guid previousId,
    RouteWorkSnapshot current,
    TruckRouteProfile profile,
    IReadOnlyDictionary<Guid, DeadheadHistorySnapshot> history,
    CancellationToken ct
  )
  {
    var saved = await db
      .DispatchDeadheads.AsNoTracking()
      .SingleOrDefaultAsync(
        x =>
          x.DispatchId == current.Id
          && x.ExecutionLegId == current.ExecutionLegId,
        ct
      );
    var pair = DeadheadConnection.Find(history.GetValueOrDefault(current.Id));
    var route =
      pair?.Previous.Id == previousId ? pair.ReadRoute(saved, profile) : null;
    var road = SavedRoadVersion.Connection(
      NextLoadRouteVersion.From(
        current.Id,
        current.ExecutionLegId,
        new(current.Id, null, saved)
      )
    );
    return (route, road);
  }

  private Task<Dictionary<Guid, DispatchDeadhead>> ReadSavedAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct
  ) =>
    db
      .DispatchDeadheads.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId) && x.ExecutionLegId == null)
      .ToDictionaryAsync(x => x.DispatchId, ct);

  public Task<TruckRoute?> ReadRouteAsync(
    Guid previousId,
    Load current,
    TruckRouteProfile profile,
    CancellationToken ct
  ) =>
    ReadRouteAsync(
      previousId,
      RouteWorkProjection.Capture(current),
      profile,
      ct
    );

  public async Task<TruckRoute?> ReadRouteAsync(
    Guid previousId,
    RouteWorkSnapshot current,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var routes = await ReadRoutesAsync(current.TruckId, [current], profile, ct);
    return
      routes.TryGetValue(current.Id, out var route)
      && route.PreviousId == previousId
      ? route.Route
      : null;
  }

  public async Task<
    Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>
  > ReadRoutesAsync(
    Guid? truckId,
    IReadOnlyCollection<RouteWorkSnapshot> loads,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var result = new Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>();
    if (truckId is null || loads.Count == 0)
      return result;
    var history = await historyReader.ReadLoadedAsync(loads, ct);
    return await ReadRoutesAsync(loads, profile, history, ct);
  }

  public async Task<
    Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>
  > ReadRoutesAsync(
    IReadOnlyCollection<RouteWorkSnapshot> loads,
    TruckRouteProfile profile,
    IReadOnlyDictionary<Guid, DeadheadHistorySnapshot> history,
    CancellationToken ct
  )
  {
    var result = new Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>();
    var ids = loads.Select(x => x.Id).Distinct().ToArray();
    var saved = await db
      .DispatchDeadheads.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId))
      .ToDictionaryAsync(x => (x.DispatchId, x.ExecutionLegId), ct);
    foreach (var load in loads)
    {
      var pair = DeadheadConnection.Find(history.GetValueOrDefault(load.Id));
      var route = pair?.ReadRoute(
        saved.GetValueOrDefault((load.Id, load.ExecutionLegId)),
        profile
      );
      if (route is not null)
        result[load.Id] = (pair!.Previous.Id, route);
    }
    return result;
  }

  public async Task ReadAsync(
    IReadOnlyCollection<DispatchResponse> items,
    CancellationToken ct
  )
  {
    if (items.Count == 0)
      return;
    var ids = items.Select(x => x.Id).ToArray();
    var history = await ReadHistoryAsync(ids, ct);
    var saved = await ReadSavedAsync(ids, ct);
    var rates = await db
      .DispatchRates.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId))
      .ToDictionaryAsync(x => x.DispatchId, ct);
    var profiles = new Dictionary<Guid, TruckRouteProfile>();
    foreach (var item in items)
    {
      item.EmptyMiles = null;
      item.EmptyMilesStatus = "unavailable";
      item.LoadedRatePerMile = DispatchRates.PerMile(
        item.Price,
        item.LoadedMiles
      );
      item.TotalRatePerMile = null;
      var snapshot = history.GetValueOrDefault(item.Id);
      var load = snapshot?.Current;
      var pair = DeadheadConnection.Find(snapshot);
      if (pair is null)
        continue;
      var truck = load!.TruckId!.Value;
      if (!profiles.TryGetValue(truck, out var profile))
        profiles[truck] = profile = await plans.ProfileAsync(truck, ct);
      item.EmptyMilesStatus = "pending";
      if (
        saved.TryGetValue(item.Id, out var entry)
        && entry.InputHash == pair.Signature(profile)
      )
      {
        item.EmptyMiles = entry.Miles;
        item.EmptyMilesStatus = entry.Miles.HasValue ? "ready" : "unavailable";
        if (
          rates.TryGetValue(item.Id, out var rate)
          && DispatchRates.Matches(
            rate,
            RateInputs(load),
            entry.Miles,
            entry.InputHash
          )
          && rate.Price == item.Price
          && rate.LoadedMiles == item.LoadedMiles
          && rate.Currency == item.Currency
        )
        {
          item.LoadedRatePerMile = rate.LoadedRatePerMile;
          item.TotalRatePerMile = rate.TotalRatePerMile;
        }
        else if (entry.Miles is >= 0 && item.LoadedMiles is > 0)
          item.TotalRatePerMile = DispatchRates.PerMile(
            item.Price,
            item.LoadedMiles + entry.Miles
          );
      }
    }
  }

  public Task EnsureAsync(
    Load source,
    TruckRouteProfile profile,
    CancellationToken ct
  ) => EnsureAsync(RouteWorkProjection.Capture(source), profile, ct);

  public async Task EnsureAsync(
    RouteWorkSnapshot source,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    profile = profile.Copy();
    if (source.Status is not ("assigned" or "in_transit" or "unassigned"))
      return;
    var captured = await historyReader.ReadAsync(source, ct);
    var latestLoad = captured?.Current;
    if (
      latestLoad is null
      || latestLoad.Status is not ("assigned" or "in_transit" or "unassigned")
    )
      return;
    var load = latestLoad;
    var pair = DeadheadConnection.Find(captured);
    var hash =
      pair is not null && profile.Validate() is null
        ? pair.Signature(profile)
        : "";
    var gate = Gates.For(load.TruckId ?? load.Id);
    await GateWait.WaitAsync(gate, "Deadhead", ct);
    DispatchDeadhead? saved = null;
    var publishing = false;
    try
    {
      await using (
        var reservation = await publication.BeginAsync(captured!, ct)
      )
      {
        publishing = true;
        saved = await db.DispatchDeadheads.SingleOrDefaultAsync(
          x =>
            x.DispatchId == load.Id && x.ExecutionLegId == load.ExecutionLegId,
          ct
        );
        if (load.ExecutionLegId is null)
          await financials.SaveAsync(
            RateInputs(load),
            hash.Length > 0 && saved?.InputHash == hash ? saved.Miles : null,
            hash,
            ct
          );
        if (
          pair is null
          || hash.Length == 0
          || !routing.IsConfigured
          || load.Status == "unassigned"
        )
        {
          await reservation.CommitAsync(ct);
          return;
        }
        var sameInputs = saved?.InputHash == hash;
        if (
          saved is not null
          && sameInputs
          && (
            saved.RetryAfter > DateTime.UtcNow
            || saved.Miles.HasValue
              && pair.ReadRoute(saved, profile) is not null
          )
        )
        {
          await reservation.CommitAsync(ct);
          return;
        }
        if (saved is null)
        {
          saved = new()
          {
            Id = Guid.NewGuid(),
            DispatchId = load.Id,
            ExecutionLegId = load.ExecutionLegId,
          };
          db.DispatchDeadheads.Add(saved);
        }
        saved.InputHash = hash;
        saved.PreviousDispatchId = pair.Previous.Id;
        saved.PreviousExecutionLegId = pair.Previous.ExecutionLegId;
        // Missing geometry must not erase valid financial mileage for the same
        // inputs.
        if (!sameInputs)
          saved.Miles = null;
        saved.RouteJson = null;
        if (!sameInputs)
          saved.CalculatedAt = null;
        // Persist the retry budget before billable provider requests.
        saved.ErrorMessage = null;
        saved.RetryAfter = DateTime.UtcNow.AddMinutes(5);
        await db.SaveChangesAsync(ct);
        await reservation.CommitAsync(ct);
      }
      publishing = false;
      TruckRoute route;
      try
      {
        var from = await StopLocation.ResolveAsync(pair.From, routing, ct);
        var to = await StopLocation.ResolveAsync(pair.To, routing, ct);
        route = await routing.CalculateAsync([from, to], profile, ct);
        if (!SavedRouteGeometry.Complete(route, 1))
          throw new RoutePlanningException(
            "Empty route geometry is incomplete.",
            saved.RetryAfter
          );
        if (!RouteAnchoring.Matches(route, [from, to]))
          throw new RoutePlanningException(
            "The empty route does not reach the confirmed stops.",
            saved.RetryAfter
          );
      }
      catch (RoutePlanningException ex)
      {
        saved.ErrorMessage = ex.Message;
        saved.RetryAfter = ex.RetryAfter;
        await db.SaveChangesAsync(ct);
        throw;
      }
      await using var transaction = await publication.BeginAsync(captured!, ct);
      publishing = true;
      if (pair!.Signature(profile) != hash)
        throw new RoutePlanningException("The connection inputs changed.");
      saved!.Miles = (decimal)route.Miles;
      saved.RouteJson = RoutePlanStorage.Serialize(route);
      saved.CalculatedAt = DateTime.UtcNow;
      saved.RetryAfter = DateTime.MinValue;
      saved.ErrorMessage = null;
      await db.SaveChangesAsync(ct);
      if (load.ExecutionLegId is null)
        await financials.SaveAsync(RateInputs(load), saved.Miles, hash, ct);
      await transaction.CommitAsync(ct);
    }
    catch (DbUpdateConcurrencyException ex)
      when (saved is not null
        && ex.Entries.Count > 0
        && ex.Entries.All(entry => ReferenceEquals(entry.Entity, saved))
      )
    {
      // Another worker owns the persisted reservation or its completed result.
      DetachResults(load.Id, saved);
    }
    catch when (publishing)
    {
      DetachResults(load.Id, saved);
      throw;
    }
    finally
    {
      gate.Release();
    }
  }

  private static DispatchRateInputs RateInputs(RouteWorkSnapshot load) =>
    new(load.Id, load.Price, load.LoadedMiles, load.Currency);

  private void DetachResults(Guid dispatchId, DispatchDeadhead? saved)
  {
    // Rolled-back values must not be reused or saved by a later operation.
    if (saved is not null)
      db.Entry(saved).State = EntityState.Detached;
    foreach (
      var rate in db
        .DispatchRates.Local.Where(x => x.DispatchId == dispatchId)
        .ToArray()
    )
      db.Entry(rate).State = EntityState.Detached;
  }
}
