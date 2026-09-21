using Application.Caching;
using Application.Features.Execution.Interfaces;
using Application.Features.Mileage.Interfaces;
using Application.Features.Routing.Background;
using Application.Features.Routing.Commands;
using Domain.Entities.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Execution.Background;

public sealed class ExecutionPlanningOperation(
  IServiceScopeFactory scopes,
  TimeProvider clock,
  ILogger<ExecutionPlanningOperation> logger
) : IExecutionPlanningOperation
{
  public async Task RunAsync(CancellationToken stoppingToken)
  {
    var nextPrune = DateTime.MinValue;
    while (!stoppingToken.IsCancellationRequested)
    {
      ExecutionPlanningChange? work = null;
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IExecutionPlanningStore>();
        var now = clock.GetUtcNow().UtcDateTime;
        if (now >= nextPrune)
        {
          await store.PruneAsync(now.AddDays(-7), stoppingToken);
          nextPrune = now.AddHours(1);
        }
        work = await store.ClaimAsync(now, stoppingToken);
        if (work is null)
        {
          await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
          continue;
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
          stoppingToken
        );
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        var succeeded = await ProcessAsync(services, work, timeout.Token);
        await store.CompleteAsync(
          work,
          clock.GetUtcNow().UtcDateTime,
          succeeded,
          stoppingToken
        );
      }
      catch (OperationCanceledException)
        when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (OperationCanceledException)
      {
        await RetryAsync(work, stoppingToken);
      }
      catch (Exception ex)
      {
        logger.LogWarning(
          ex,
          "Execution planning change {OperationId} will retry",
          work?.Id
        );
        await RetryAsync(work, stoppingToken);
      }
    }
  }

  private static async Task<bool> ProcessAsync(
    IServiceProvider services,
    ExecutionPlanningChange work,
    CancellationToken ct
  )
  {
    var db = services.GetRequiredService<IAppDbContext>();
    var leg = await db
      .ExecutionLegs.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == work.ExecutionLegId, ct);
    if (work.MileageOnly)
    {
      if (
        leg is null
        || leg.Revision != work.AssignmentRevision
        || leg.Status == "cancelled"
      )
        return true;
      return await services
        .GetRequiredService<IAutomaticMileageRecorder>()
        .CapturePlannedAsync(work.ExecutionLegId, ct);
    }
    var reads = services.GetRequiredService<ReadCache>();
    foreach (
      var key in new[] { "dispatch", "board", "execution", "route-previews" }
    )
      reads.Invalidate(key);
    reads.Invalidate($"route:{work.DispatchId}");
    reads.Invalidate($"route:{work.DispatchId}:leg:{work.ExecutionLegId}");
    var queue = services.GetRequiredService<RoutePreparationQueue>();
    queue.MarkDirty(work.DispatchId);
    queue.MarkTruckDirty(work.TruckId);
    if (
      leg is null
      || leg.Revision != work.AssignmentRevision
      || leg.Status is "cancelled" or "completed"
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
    if (
      !result.Success
      || result.Response?.State?.Plan is null
      || result.Response.State.Plan.InputsChanged
    )
      return false;
    return await services
      .GetRequiredService<IAutomaticMileageRecorder>()
      .CapturePlannedAsync(work.ExecutionLegId, ct);
  }

  private async Task RetryAsync(
    ExecutionPlanningChange? work,
    CancellationToken ct
  )
  {
    if (work is not null)
    {
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        await scope
          .ServiceProvider.GetRequiredService<IExecutionPlanningStore>()
          .CompleteAsync(work, clock.GetUtcNow().UtcDateTime, false, ct);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        // The durable lease expires if the database is unavailable, so the
        // work is not lost - but nothing else would ever say why a pass
        // stopped doing anything.
        logger.LogError(ex, "Execution planning pass failed");
      }
    }
    await Task.Delay(TimeSpan.FromSeconds(5), ct);
  }
}
