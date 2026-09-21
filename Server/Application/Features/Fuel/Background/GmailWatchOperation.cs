using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Features.Fuel.Background;

public sealed class GmailWatchOperation(
  IServiceScopeFactory scopes,
  ILogger<GmailWatchOperation> logger
) : IGmailWatchOperation
{
  public async Task RunAsync(CancellationToken ct)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
    while (await timer.WaitForNextTickAsync(ct))
    {
      var runId = Guid.NewGuid();
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        // Each carrier watches its own mailbox, so maintenance runs once
        // for each of them.
        GmailWatchRunResult result = new();
        await CompanyPasses.ForEachCompanyAsync(
          scope.ServiceProvider,
          async token =>
            result = await scope
              .ServiceProvider.GetRequiredService<GmailWatchLifecycle>()
              .RunAsync(false, token),
          ct
        );
        if (result.RenewalError is not null || result.RecoveryError is not null)
          logger.LogWarning(
            "Gmail maintenance {RunId} failed; renewal {RenewalErrorCode}, recovery {RecoveryErrorCode}; persisted retry schedule retained",
            runId,
            result.RenewalError,
            result.RecoveryError
          );
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        logger.LogWarning(
          "Gmail maintenance {RunId} checkpoint failed with {ErrorCode}; retry delayed",
          runId,
          ex.GetType().Name
        );
        await Task.Delay(TimeSpan.FromMinutes(5), ct);
      }
    }
  }
}
