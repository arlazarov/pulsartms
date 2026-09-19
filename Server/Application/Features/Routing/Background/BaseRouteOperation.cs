using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Background;

public sealed class BaseRouteOperation(
  IServiceScopeFactory scopes,
  ILogger<BaseRouteOperation> logger,
  RoutePreparationQueue queue,
  IOptions<RoutePreparationOptions> options,
  TimeProvider time
) : IBaseRouteOperation
{
  private int offset;
  private DateTime nextPrune;

  public async Task RunAsync(CancellationToken ct)
  {
    using var timer = new PeriodicTimer(
      TimeSpan.FromSeconds(options.Value.TickSeconds),
      time
    );
    do
    {
      await RunOnceAsync(ct);
    } while (await timer.WaitForNextTickAsync(ct));
  }

  public async Task RunOnceAsync(CancellationToken ct)
  {
    try
    {
      await ScanAsync(ct);
      await TransferHintsAsync(ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Route preparation repair scan failed");
    }
    for (var index = 0; index < options.Value.BatchSize; index++)
    {
      ct.ThrowIfCancellationRequested();
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        var store =
          scope.ServiceProvider.GetRequiredService<ISourceRoadStore>();
        var work = await store.ClaimAsync(
          time.GetUtcNow().UtcDateTime,
          TimeSpan.FromMinutes(3),
          ct
        );
        if (work is null)
          break;
        await PrepareAsync(work, ct);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Route preparation delivery failed");
        break;
      }
    }
  }

  private async Task TransferHintsAsync(CancellationToken ct)
  {
    for (var index = 0; index < options.Value.ScanPageSize; index++)
    {
      var hint = queue.Take(1).FirstOrDefault();
      if (hint is null)
        break;
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        var input = await scope
          .ServiceProvider.GetRequiredService<SourceRoadInputs>()
          .ReadAsync(hint.DispatchId, ct);
        await scope
          .ServiceProvider.GetRequiredService<ISourceRoadStore>()
          .ObserveAsync(
            hint.DispatchId,
            input?.TruckId,
            input?.Signature ?? "missing",
            hint.Priority,
            hint.Explicit,
            true,
            time.GetUtcNow().UtcDateTime,
            ct
          );
        queue.Complete(hint, input?.Signature ?? "missing", input?.TruckId);
      }
      catch
      {
        queue.Retry(hint, hint.Fingerprint, null);
        throw;
      }
    }
  }

  private async Task ScanAsync(CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    var store = scope.ServiceProvider.GetRequiredService<ISourceRoadStore>();
    var inputs = scope.ServiceProvider.GetRequiredService<SourceRoadInputs>();
    var now = time.GetUtcNow().UtcDateTime;
    if (now >= nextPrune)
    {
      await store.PruneAsync(now.AddDays(-7), ct);
      nextPrune = now.AddHours(1);
    }
    await scope
      .ServiceProvider.GetRequiredService<StopAddressService>()
      .ExpireAsync(ct);
    var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
    var horizon = today.AddDays(options.Value.HorizonDays);
    var prewarm = today.AddDays(options.Value.PrewarmDays);
    var loads = await db
      .Dispatches.AsNoTracking()
      .Where(x =>
        db.LoadExecutionLegs.Any(link =>
          link.DispatchId == x.Id
          && (
            link.ExecutionLeg.Status == "active"
            || link.ExecutionLeg.Status == "planned"
          )
        )
        || x.Stops.Count >= 2
          && (
            x.Status == "in_transit"
            || (x.Status == "assigned" || x.Status == "unassigned")
              && (
                x.DeliveryDate
                ?? x.Stops.OrderByDescending(s => s.Sequence)
                  .Select(s => s.ScheduledDate)
                  .FirstOrDefault()
                ?? x.ShipDate
                ?? today
              ) >= today
              && (
                x.Stops.OrderBy(s => s.Sequence)
                  .Select(s => s.ScheduledDate)
                  .FirstOrDefault()
                ?? x.ShipDate
                ?? today
              ) <= (x.Status == "assigned" ? horizon : prewarm)
          )
      )
      .OrderBy(x =>
        x.Status == "in_transit" ? 0
        : x.Status == "assigned" ? 1
        : 2
      )
      .ThenBy(x =>
        x.Stops.OrderBy(s => s.Sequence)
          .Select(s => s.ScheduledDate)
          .FirstOrDefault() ?? x.ShipDate
      )
      .ThenBy(x => x.Id)
      .Skip(offset)
      .Take(options.Value.ScanPageSize)
      .Select(x => new { x.Id, x.Status })
      .ToListAsync(ct);
    offset =
      loads.Count < options.Value.ScanPageSize ? 0 : offset + loads.Count;
    var observations = await inputs.ReadAsync(
      loads.Select(x => x.Id).ToArray(),
      ct
    );
    foreach (var load in loads)
    {
      var input = observations.GetValueOrDefault(load.Id);
      if (input is null)
        continue;
      await store.ObserveAsync(
        load.Id,
        input.TruckId,
        input.Signature,
        load.Status == "in_transit" ? 0
          : load.Status == "unassigned" ? 3
          : 1,
        false,
        false,
        now,
        ct
      );
    }
  }

  private async Task PrepareAsync(SourceRoadWork work, CancellationToken ct)
  {
    Guid? truckId = work.TruckId;
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromMinutes(2));
    try
    {
      await using var scope = scopes.CreateAsyncScope();
      var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
      var observation = await scope
        .ServiceProvider.GetRequiredService<SourceRoadInputs>()
        .ReadAsync(work.DispatchId, timeout.Token);
      if (observation is not null)
        await scope
          .ServiceProvider.GetRequiredService<ISourceRoadStore>()
          .ObserveAsync(
            work.DispatchId,
            observation.TruckId,
            observation.Signature,
            3,
            work.Explicit,
            false,
            time.GetUtcNow().UtcDateTime,
            timeout.Token
          );
      var token = timeout.Token;
      var load = await db
        .Dispatches.AsNoTracking()
        .Include(x => x.Stops)
        .SingleOrDefaultAsync(x => x.Id == work.DispatchId, token);
      if (load is null)
      {
        await FinishAsync(work, true, null, ct);
        return;
      }
      var native = await ExecutionRouteSections.ReadAsync(db, [load], token);
      if (native.TryGetValue(load.Id, out var sections))
      {
        var addresses =
          scope.ServiceProvider.GetRequiredService<ExecutionStopAddressService>();
        foreach (var legId in sections.Select(x => x.ExecutionLegId!.Value))
          await addresses.VerifyAsync(legId, token);
        native = await ExecutionRouteSections.ReadAsync(db, [load], token);
        sections = native[load.Id];
        var pending = sections
          .Where(x => x.Stops.Length >= 2)
          .OrderBy(x => x.ExecutionStatus == "active" ? 0 : 1)
          .ThenBy(x => x.ExecutionLegId)
          .ToArray();
        queue.SetTrucks(load.Id, sections.Select(x => x.TruckId!.Value));
        var nativePlans =
          scope.ServiceProvider.GetRequiredService<RoutePlanningService>();
        var bases =
          scope.ServiceProvider.GetRequiredService<BaseRouteService>();
        RoutePlanningException? failure = null;
        foreach (var leg in pending)
        {
          try
          {
            var nativeProfile = await nativePlans.ProfileAsync(
              leg.TruckId!.Value,
              token
            );
            await bases.EnsureAsync(leg, nativeProfile, token);
            await scope
              .ServiceProvider.GetRequiredService<DeadheadService>()
              .EnsureAsync(leg, nativeProfile, token);
            var nativeRetry = await db
              .DispatchDeadheads.AsNoTracking()
              .Where(x =>
                x.DispatchId == leg.Id && x.ExecutionLegId == leg.ExecutionLegId
              )
              .Select(x => x.RetryAfter)
              .SingleOrDefaultAsync(token);
            if (nativeRetry > time.GetUtcNow().UtcDateTime)
              throw new RoutePlanningException(
                "Deadhead preparation is pending.",
                nativeRetry
              );
          }
          catch (RoutePlanningException ex)
          {
            failure ??= ex;
          }
        }
        if (failure is not null)
          throw failure;
        await FinishAsync(work, true, null, ct);
        return;
      }

      truckId = RoutePreparationInputs.Truck(load, work.TruckId);
      if (!Eligible(load, work.Explicit))
      {
        await FinishAsync(work, true, null, ct);
        return;
      }
      var plans =
        scope.ServiceProvider.GetRequiredService<RoutePlanningService>();
      load = load.TruckItinerary();
      await scope
        .ServiceProvider.GetRequiredService<StopAddressService>()
        .VerifyAsync(load.Stops, token);
      var resolved =
        load.Status != "unassigned"
          ? await plans.ResolveAssignmentAsync(load, token)
          : RouteWorkProjection.Capture(load);
      truckId = resolved.TruckId;
      var addressRetry = resolved
        .Stops.Where(x =>
          !string.IsNullOrWhiteSpace(x.Address)
          && StopLocation.ReliablePoint(x, time.GetUtcNow().UtcDateTime) is null
          && x.AddressRetryAfter > time.GetUtcNow().UtcDateTime
        )
        .Select(x => x.AddressRetryAfter)
        .Min();
      if (addressRetry is not null)
        throw new RoutePlanningException(
          "Stop address verification is pending.",
          addressRetry
        );
      var profile = await plans.ProfileAsync(truckId ?? Guid.Empty, token);
      await scope
        .ServiceProvider.GetRequiredService<BaseRouteService>()
        .EnsureAsync(resolved, profile, token);
      await scope
        .ServiceProvider.GetRequiredService<DeadheadService>()
        .EnsureAsync(resolved, profile, token);
      var retry = await db
        .DispatchDeadheads.AsNoTracking()
        .Where(x => x.DispatchId == load.Id && x.ExecutionLegId == null)
        .Select(x => x.RetryAfter)
        .SingleOrDefaultAsync(token);
      if (retry > time.GetUtcNow().UtcDateTime)
        throw new RoutePlanningException(
          "Deadhead preparation is pending.",
          retry
        );
      await FinishAsync(work, true, null, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (RoutePlanningException ex)
    {
      await FinishAsync(work, false, ex.RetryAfter, ct);
    }
    catch (OperationCanceledException)
    {
      await FinishAsync(work, false, null, ct);
      logger.LogWarning(
        "Route preparation timed out for {DispatchId}",
        work.DispatchId
      );
    }
    catch (Exception ex)
    {
      await FinishAsync(work, false, null, ct);
      logger.LogWarning(
        ex,
        "Route preparation failed for {DispatchId}",
        work.DispatchId
      );
    }
  }

  private async Task FinishAsync(
    SourceRoadWork work,
    bool succeeded,
    DateTime? retryAfter,
    CancellationToken ct
  )
  {
    await using var scope = scopes.CreateAsyncScope();
    var now = time.GetUtcNow().UtcDateTime;
    var next =
      succeeded ? now.AddMinutes(options.Value.RepairMinutes)
      : retryAfter is { } retry && retry != DateTime.MaxValue && retry > now
        ? retry
      : now.AddSeconds(
        Math.Min(
          3600,
          options.Value.RetrySeconds
            * Math.Pow(2, Math.Min(work.Attempts - 1, 6))
        )
      );
    await scope
      .ServiceProvider.GetRequiredService<ISourceRoadStore>()
      .CompleteAsync(work, succeeded, now, next, ct);
  }

  private bool Eligible(Load load, bool requested)
  {
    if (load.Status is not ("assigned" or "in_transit" or "unassigned"))
      return false;
    if (requested || load.Status == "in_transit")
      return true;
    var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
    var start =
      load.Stops.OrderBy(x => x.Sequence).FirstOrDefault()?.ScheduledDate
      ?? load.ShipDate
      ?? today;
    var end =
      load.DeliveryDate
      ?? load.Stops.OrderByDescending(x => x.Sequence)
        .FirstOrDefault()
        ?.ScheduledDate
      ?? load.ShipDate
      ?? today;
    return end >= today
      && start
        <= today.AddDays(
          load.Status == "assigned"
            ? options.Value.HorizonDays
            : options.Value.PrewarmDays
        );
  }
}
