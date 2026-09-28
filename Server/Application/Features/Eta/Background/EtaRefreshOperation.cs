using Application.Diagnostics;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Rules;
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
    // Forecasts are due only while someone views them, so the loop may
    // rightly wait for ever; a round of due forecasts, two at a time with
    // 30 seconds each, is stale past ten minutes.
    BackgroundProgress.OnDemand("EtaRefresh", TimeSpan.FromMinutes(10));
    while (!ct.IsCancellationRequested)
    {
      await memory.WaitForRefreshAsync(ct);
      BackgroundProgress.Started("EtaRefresh");
      try
      {
        await RefreshDueAsync(ct);
      }
      finally
      {
        BackgroundProgress.Finished("EtaRefresh");
      }
    }
  }

  private Task RefreshDueAsync(CancellationToken ct) =>
    Parallel.ForEachAsync(
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
        // A planning pass holds the truck's inputs: the next demand
        // refreshes it, and contention is not a failure.
        catch (RoutePlanningException busy) when (busy.Busy)
        {
          logger.LogDebug(
            "ETA refresh for {DispatchId} deferred: planning inputs busy",
            id
          );
        }
        catch (Exception ex)
        {
          logger.LogWarning(ex, "ETA refresh failed for {DispatchId}", id);
        }
      }
    );
}
