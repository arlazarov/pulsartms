using Application.Features.Eta.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Eta.Background;

public sealed class EtaRefreshOperation(
  EtaMemory memory,
  IDriverHosProvider hos,
  IServiceScopeFactory scopes,
  ILogger<EtaRefreshOperation> logger
) : IEtaRefreshOperation
{
  public async Task RunAsync(CancellationToken ct)
  {
    // A duty change is one of the events that makes a forecast due before
    // its interval runs out.
    hos.DutyChanged += memory.DutyChanged;
    try
    {
      await RefreshAsync(ct);
    }
    finally
    {
      hos.DutyChanged -= memory.DutyChanged;
    }
  }

  private async Task RefreshAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      await memory.WaitForRefreshAsync(ct);
      await Parallel.ForEachAsync(
        memory.Due(DateTime.UtcNow).ToArray(),
        new ParallelOptions
        {
          MaxDegreeOfParallelism = 2,
          CancellationToken = ct,
        },
        async (id, stopping) =>
        {
          try
          {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
              stopping
            );
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await using var scope = scopes.CreateAsyncScope();
            // The queue holds ids without saying whose they are, so the
            // refresh is offered to each carrier in turn and the filters
            // decide which of them the load actually belongs to.
            await CompanyPasses.ForEachCompanyAsync(
              scope.ServiceProvider,
              token =>
                scope
                  .ServiceProvider.GetRequiredService<EtaForecastService>()
                  .RefreshAsync(id, token),
              timeout.Token
            );
          }
          catch (OperationCanceledException)
            when (stopping.IsCancellationRequested) { }
          catch (OperationCanceledException)
          {
            logger.LogWarning("ETA refresh timed out for {DispatchId}", id);
          }
          catch (Exception ex)
          {
            logger.LogWarning(ex, "ETA refresh failed for {DispatchId}", id);
          }
        }
      );
    }
  }
}
