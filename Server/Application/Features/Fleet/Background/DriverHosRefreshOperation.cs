using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Services;
using Application.Features.Synchronization.Interfaces;
using Application.Features.Synchronization.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Features.Fleet.Background;

public sealed class DriverHosRefreshOperation(
  DriverHosSnapshot snapshot,
  IServiceScopeFactory scopes,
  IOptions<SynchronizationOptions> options,
  ISynchronizationStatusProvider synchronization,
  ILogger<DriverHosRefreshOperation> logger
) : IDriverHosRefreshOperation
{
  public async Task RunAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      await RunOnceAsync(ct);
      await snapshot.WaitForRefreshAsync(ct);
    }
  }

  public async Task RunOnceAsync(CancellationToken ct)
  {
    if (
      !snapshot.TryBeginRefresh(
        options.Value.Enabled && synchronization.Status.Active
      )
    )
      return;
    var traceId =
      Activity.Current?.TraceId.ToString()
      ?? ActivityTraceId.CreateRandom().ToString();
    IReadOnlyDictionary<string, DriverHosClocks>? clocks = null;
    try
    {
      using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
      timeout.CancelAfter(TimeSpan.FromSeconds(30));
      await using var scope = scopes.CreateAsyncScope();
      using var timing = PerformanceStages.Start("driver-hos", "provider-wait");
      clocks = await scope
        .ServiceProvider.GetRequiredService<IDriverHosRefreshProvider>()
        .RefreshClocksAsync(timeout.Token);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (OperationCanceledException)
    {
      PerformanceStages.Count("driver-hos", "refresh-timeout", 1);
    }
    catch (Exception ex)
    {
      logger.LogWarning(
        ex,
        "Background operation {Operation} failed; retry delayed. TraceId {TraceId}",
        "driver-hos",
        traceId
      );
    }
    finally
    {
      snapshot.Complete(clocks);
    }
  }
}
