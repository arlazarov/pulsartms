using Application.Features.Eta.Services;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Eta.Background;

public sealed class EtaRefreshOperation(
  EtaMemory memory,
  IServiceScopeFactory scopes,
  ILogger<EtaRefreshOperation> logger
) : IEtaRefreshOperation
{
  public async Task RunAsync(CancellationToken ct)
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
            await scope
              .ServiceProvider.GetRequiredService<EtaForecastService>()
              .RefreshAsync(id, timeout.Token);
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
