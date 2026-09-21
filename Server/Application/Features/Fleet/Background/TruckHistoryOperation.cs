using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Interfaces;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Fleet.Background;

public sealed class TruckHistoryOperation(
  TruckHistoryQueue queue,
  IServiceScopeFactory scopes,
  ILogger<TruckHistoryOperation> logger
) : ITruckHistoryOperation
{
  public async Task RunAsync(CancellationToken ct)
  {
    await foreach (var query in queue.ReadAsync(ct))
    {
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        // Nothing here names a carrier, so the pass is run once for each
        // of them and each run sees only that carrier's trucks.
        await CompanyPasses.ForEachCompanyAsync(
          scope.ServiceProvider,
          token =>
            scope
              .ServiceProvider.GetRequiredService<ISender>()
              .Send(query with { Refresh = true }, token),
          timeout.Token
        );
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        logger.LogWarning(
          ex,
          "Truck history refresh failed for {TruckId}",
          query.TruckId
        );
      }
      finally
      {
        queue.Complete(query);
      }
    }
  }
}
