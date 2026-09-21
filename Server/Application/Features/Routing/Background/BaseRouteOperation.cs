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

public sealed partial class BaseRouteOperation(
  IServiceScopeFactory scopes,
  ILogger<BaseRouteOperation> logger,
  RoutePreparationQueue queue,
  IOptions<RoutePreparationOptions> options,
  TimeProvider time
) : IBaseRouteOperation
{
  private readonly Dictionary<Guid, int> offsets = [];
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
      await using var scope = scopes.CreateAsyncScope();
      await CompanyPasses.ForEachCompanyAsync(
        scope.ServiceProvider,
        ScanAsync,
        ct
      );
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
        // From here the pass belongs to the carrier whose work was
        // claimed. A host with no notion of carriers has nothing to switch
        // to, and does not. Every read it makes is narrowed to them and every row
        // it writes is stamped with them, the same as if one of their
        // dispatchers had asked for it.
        using var serving = scope
          .ServiceProvider.GetService<ICurrentCompany>()
          ?.As(work.Company);
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
        SourceRoadObservation? input = null;
        await CompanyPasses.ForEachCompanyAsync(
          scope.ServiceProvider,
          async token =>
          {
            if (input is not null)
              return;
            await using var owned = scopes.CreateAsyncScope();
            input = await owned
              .ServiceProvider.GetRequiredService<SourceRoadInputs>()
              .ReadAsync(hint.DispatchId, token);
            if (input is null)
              return;
            await owned
              .ServiceProvider.GetRequiredService<ISourceRoadStore>()
              .ObserveAsync(
                hint.DispatchId,
                input.TruckId,
                input.Signature,
                hint.Priority,
                hint.Explicit,
                true,
                time.GetUtcNow().UtcDateTime,
                token
              );
          },
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
    var company =
      scope.ServiceProvider.GetService<ICurrentCompany>()?.Id ?? Guid.Empty;
    var offset = offsets.GetValueOrDefault(company);
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
    offsets[company] =
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
