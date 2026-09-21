using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Background;

// Preparing one load's road in the background: the work is claimed, the
// road is built under a timeout, and whatever happens the claim is
// settled so the next pass knows whether to try again.
public sealed partial class BaseRouteOperation
{
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
}
