using Application.Diagnostics;
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
    BackgroundProgress.Expect("GmailWatch", TimeSpan.FromMinutes(1));
    while (await timer.WaitForNextTickAsync(ct))
    {
      BackgroundProgress.Started("GmailWatch");
      var runId = Guid.NewGuid();
      try
      {
        await using var scope = scopes.CreateAsyncScope();
        // Each carrier watches its own mailbox, so maintenance runs once
        // for each of them and each says for itself how it went. One result
        // kept across the loop used to be overwritten by the next carrier's,
        // and amfcarrier's failures were never logged.
        var companies = scope.ServiceProvider.GetService<ICurrentCompany>();
        await CompanyPasses.ForEachCompanyAsync(
          scope.ServiceProvider,
          async token =>
          {
            var result = await scope
              .ServiceProvider.GetRequiredService<GmailWatchLifecycle>()
              .RunAsync(false, token);
            if (
              result.RenewalError is not null
              || result.RecoveryError is not null
            )
              logger.LogWarning(
                "Gmail maintenance {RunId} for company {CompanyId} failed; renewal {RenewalErrorCode}, recovery {RecoveryErrorCode}; persisted retry schedule retained",
                runId,
                companies?.Id,
                result.RenewalError,
                result.RecoveryError
              );
          },
          ct
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
