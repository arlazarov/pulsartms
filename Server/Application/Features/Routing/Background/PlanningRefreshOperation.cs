using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Options;
using Application.Features.Routing.Commands;
using MediatR;
using Application.Features.Routing.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Background;

public sealed class PlanningRefreshOperation(PlanningRefreshQueue queue, IServiceScopeFactory scopes,
  IOptions<SynchronizationOptions> options, ILogger<PlanningRefreshOperation> logger) : IPlanningRefreshOperation
{
  public async Task RunAsync(CancellationToken stoppingToken)
  {
    await foreach (var id in queue.ReadAllAsync(stoppingToken))
    {
      var succeeded = false;
      try
      {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.JobTimeoutSeconds));
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ISender>()
          .Send(new PrepareDispatchPlanningCommand(id), timeout.Token);
        succeeded = result.Success && result.Response?.State?.Plan is not null && result.Response.Message is null;
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
      catch (Exception ex) { logger.LogWarning(ex, "Background route refresh for {DispatchId} failed", id); }
      finally { queue.Complete(id, succeeded); }
    }
  }
}
