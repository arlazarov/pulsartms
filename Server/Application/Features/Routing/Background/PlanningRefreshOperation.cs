using Application.Diagnostics;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Background;

public sealed class PlanningRefreshOperation(
  IServiceScopeFactory scopes,
  PlanningRefreshSignal signal,
  IOptions<SynchronizationOptions> options,
  TimeProvider time,
  ILogger<PlanningRefreshOperation> logger
) : IPlanningRefreshOperation
{
  public Task RunAsync(CancellationToken stoppingToken) =>
    Task.WhenAll(
      Enumerable
        .Range(0, options.Value.PlanningConcurrency)
        .Select(index => RunConsumerAsync(index == 0, stoppingToken))
    );

  private async Task RunConsumerAsync(bool prune, CancellationToken ct)
  {
    var nextPrune = DateTime.MinValue;
    while (!ct.IsCancellationRequested)
    {
      PlanningRefreshWork? work = null;
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IPlanningRefreshStore>();
        var now = time.GetUtcNow().UtcDateTime;
        if (prune && now >= nextPrune)
        {
          await store.PruneAsync(now.AddDays(-7), ct);
          nextPrune = now.AddHours(1);
        }
        work = await store.ClaimAsync(
          now,
          TimeSpan.FromSeconds(options.Value.JobTimeoutSeconds + 60),
          ct
        );
        if (work is null)
        {
          await signal.WaitAsync(ct);
          continue;
        }
        signal.Pulse();
        using var measurement = PerformanceStages.Start(
          "planning",
          "background-job"
        );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(
          TimeSpan.FromSeconds(options.Value.JobTimeoutSeconds)
        );
        var succeeded = await ProcessAsync(services, work.Scope, timeout.Token);
        await CompleteAsync(store, work, succeeded, ct);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        break;
      }
      catch (OperationCanceledException)
      {
        logger.LogWarning(
          "Background route refresh {RequestId} timed out",
          work?.Id
        );
        await RetryAsync(work, ct);
      }
      catch (Exception ex)
      {
        logger.LogWarning(
          ex,
          "Background route refresh {RequestId} failed",
          work?.Id
        );
        await RetryAsync(work, ct);
      }
    }
  }

  private static async Task<bool> ProcessAsync(
    IServiceProvider services,
    PlanningScope work,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<IAppDbContext>();
    if (work.ExecutionLegId is { } legId)
    {
      if (
        !await db.LoadExecutionLegs.AnyAsync(
          x =>
            x.DispatchId == work.DispatchId
            && x.ExecutionLegId == legId
            && x.ExecutionLeg.Revision == work.AssignmentRevision
            && (
              x.ExecutionLeg.Status == "active"
              || x.ExecutionLeg.Status == "planned"
            ),
          ct
        )
      )
        return true;
    }
    else if (
      !await db.Dispatches.AnyAsync(
        x =>
          x.Id == work.DispatchId
          && (x.Status == "assigned" || x.Status == "in_transit")
          && !db.LoadExecutionLegs.Any(link => link.DispatchId == x.Id),
        ct
      )
    )
      return true;
    var result = await services
      .GetRequiredService<ISender>()
      .Send(
        new PrepareDispatchPlanningCommand(
          work.DispatchId,
          work.ExecutionLegId,
          work.AssignmentRevision
        ),
        ct
      );
    return result.Success
      && result.Response?.State?.Plan is { InputsChanged: false }
      && result.Response.Message is null;
  }

  private Task<bool> CompleteAsync(
    IPlanningRefreshStore store,
    PlanningRefreshWork work,
    bool succeeded,
    CancellationToken ct
  )
  {
    var now = time.GetUtcNow().UtcDateTime;
    var seconds = succeeded
      ? options.Value.OnDemandPlanningSeconds
      : Math.Min(
        900,
        options.Value.RetrySeconds * Math.Pow(2, Math.Min(work.Attempts - 1, 6))
      );
    return store.CompleteAsync(
      work,
      succeeded,
      now,
      now.AddSeconds(seconds),
      ct
    );
  }

  private async Task RetryAsync(PlanningRefreshWork? work, CancellationToken ct)
  {
    if (work is not null)
    {
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        await CompleteAsync(
          scope.ServiceProvider.GetRequiredService<IPlanningRefreshStore>(),
          work,
          false,
          ct
        );
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        return;
      }
      catch (Exception)
      {
        // An unacknowledged claim remains recoverable after lease expiry.
      }
    }
    try
    {
      await Task.Delay(TimeSpan.FromSeconds(5), ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
  }
}
