using Application.Caching;
using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Options;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Routing.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Background;

public sealed class BaseRouteOperation(IServiceScopeFactory scopes, ILogger<BaseRouteOperation> logger,
  RoutePreparationQueue queue, IOptions<RoutePreparationOptions> options, TimeProvider time) : IBaseRouteOperation
{
  private int offset;

  public async Task RunAsync(CancellationToken ct)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.TickSeconds), time);
    do { await RunOnceAsync(ct); }
    while (await timer.WaitForNextTickAsync(ct));
  }

  public async Task RunOnceAsync(CancellationToken ct)
  {
    try { await ScanAsync(ct); }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
    catch (Exception ex) { logger.LogWarning(ex, "Route preparation repair scan failed"); }
    for (var index = 0; index < options.Value.BatchSize; index++)
    {
      ct.ThrowIfCancellationRequested();
      if (queue.Take(1).FirstOrDefault() is not { } work) break;
      await PrepareAsync(work, ct);
    }
  }

  private async Task ScanAsync(CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    var reads = scope.ServiceProvider.GetRequiredService<ReadCache>();
    await scope.ServiceProvider.GetRequiredService<StopAddressService>().ExpireAsync(ct);
    var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
    var horizon = today.AddDays(options.Value.HorizonDays);
    var prewarm = today.AddDays(options.Value.PrewarmDays);
    var loads = await db.Dispatches.AsNoTracking()
      .Where(x => x.Stops.Count >= 2 && (x.Status == "in_transit"
        || (x.Status == "assigned" || x.Status == "unassigned")
          && (x.DeliveryDate ?? x.Stops.OrderByDescending(s => s.Sequence).Select(s => s.ScheduledDate).FirstOrDefault() ?? x.ShipDate ?? today) >= today
          && (x.Stops.OrderBy(s => s.Sequence).Select(s => s.ScheduledDate).FirstOrDefault() ?? x.ShipDate ?? today)
            <= (x.Status == "assigned" ? horizon : prewarm)))
      .OrderBy(x => x.Status == "in_transit" ? 0 : x.Status == "assigned" ? 1 : 2)
      .ThenBy(x => x.Stops.OrderBy(s => s.Sequence).Select(s => s.ScheduledDate).FirstOrDefault() ?? x.ShipDate)
      .ThenBy(x => x.Id).Skip(offset).Take(options.Value.ScanPageSize).Select(RoutePreparationInputs.Projection).ToListAsync(ct);
    offset = loads.Count < options.Value.ScanPageSize ? 0 : offset + loads.Count;
    var nextTrucks = new HashSet<Guid>();
    foreach (var load in loads)
    {
      var known = queue.Identity(load.Id);
      var truckId = RoutePreparationInputs.Truck(load, known.TruckId);
      var priority = load.Status == "in_transit" ? 0 : load.Status == "unassigned" ? 3
        : truckId is { } truck && nextTrucks.Add(truck) ? 1 : 2;
      queue.Observe(load.Id, truckId, RoutePreparationInputs.Signature(load, truckId,
        known.ConnectionVersion, reads, time.GetUtcNow().UtcDateTime), priority);
    }
  }

  private async Task PrepareAsync(RoutePreparationQueue.Work work, CancellationToken ct)
  {
    string? fingerprint = work.Fingerprint;
    Guid? truckId = null;
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromMinutes(2));
    try
    {
      await using var scope = scopes.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
      var reads = scope.ServiceProvider.GetRequiredService<ReadCache>();
      var token = timeout.Token;
      var load = await db.Dispatches.AsNoTracking().Include(x => x.Stops).SingleOrDefaultAsync(x => x.Id == work.DispatchId, token);
      if (load is null) { queue.Complete(work, "missing", null); return; }
      var sourceTruckId = load.TruckId;
      long? usedProfileGeneration = null;
      truckId = RoutePreparationInputs.Truck(load, queue.Identity(load.Id).TruckId);
      fingerprint = RoutePreparationInputs.Signature(load, truckId, work.ConnectionVersion, reads, time.GetUtcNow().UtcDateTime);
      if (!Eligible(load, work.Explicit)) { queue.Complete(work, fingerprint, truckId); return; }
      var plans = scope.ServiceProvider.GetRequiredService<RoutePlanningService>();
      if (load.Status != "unassigned") truckId = (await plans.ResolveAssignmentAsync(load, token)).TruckId;
      await scope.ServiceProvider.GetRequiredService<StopAddressService>().VerifyAsync(load.Stops, token);
      fingerprint = CurrentFingerprint();
      var addressRetry = load.Stops.Where(x => !string.IsNullOrWhiteSpace(x.Address)
        && StopLocation.VerifiedPoint(x, time.GetUtcNow().UtcDateTime) is null && x.AddressRetryAfter > time.GetUtcNow().UtcDateTime)
        .Select(x => x.AddressRetryAfter).Min();
      if (addressRetry is not null) throw new RoutePlanningException("Stop address verification is pending.", addressRetry);
      usedProfileGeneration = reads.Generation($"profile:{truckId ?? Guid.Empty}");
      var profile = await plans.ProfileAsync(truckId ?? Guid.Empty, token);
      await scope.ServiceProvider.GetRequiredService<BaseRouteService>().EnsureAsync(load, profile, token);
      await scope.ServiceProvider.GetRequiredService<DeadheadService>().EnsureAsync(load, profile, token);
      var retry = await db.DispatchDeadheads.AsNoTracking().Where(x => x.DispatchId == load.Id)
        .Select(x => x.RetryAfter).SingleOrDefaultAsync(token);
      if (retry > time.GetUtcNow().UtcDateTime) throw new RoutePlanningException("Deadhead preparation is pending.", retry);
      fingerprint = CurrentFingerprint();
      queue.Complete(work, fingerprint, truckId);

      string CurrentFingerprint()
      {
        var resolved = load.TruckId;
        load.TruckId = sourceTruckId;
        try { return RoutePreparationInputs.Signature(load, truckId, work.ConnectionVersion, reads, time.GetUtcNow().UtcDateTime, usedProfileGeneration); }
        finally { load.TruckId = resolved; }
      }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    { queue.Retry(work, fingerprint, truckId); throw; }
    catch (RoutePlanningException ex) { queue.Retry(work, fingerprint, truckId, ex.RetryAfter); }
    catch (OperationCanceledException)
    {
      queue.Retry(work, fingerprint, truckId);
      logger.LogWarning("Route preparation timed out for {DispatchId}", work.DispatchId);
    }
    catch (Exception ex)
    {
      queue.Retry(work, fingerprint, truckId);
      logger.LogWarning(ex, "Route preparation failed for {DispatchId}", work.DispatchId);
    }
  }

  private bool Eligible(Load load, bool requested)
  {
    if (load.Status is not ("assigned" or "in_transit" or "unassigned")) return false;
    if (requested || load.Status == "in_transit") return true;
    var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
    var start = load.Stops.OrderBy(x => x.Sequence).FirstOrDefault()?.ScheduledDate ?? load.ShipDate ?? today;
    var end = load.DeliveryDate ?? load.Stops.OrderByDescending(x => x.Sequence).FirstOrDefault()?.ScheduledDate ?? load.ShipDate ?? today;
    return end >= today && start <= today.AddDays(load.Status == "assigned" ? options.Value.HorizonDays : options.Value.PrewarmDays);
  }
}
