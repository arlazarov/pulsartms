using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Diagnostics.Consistency;

public interface IConsistencyAuditOperation : IBackgroundOperation;

// The periodic sweep: one bounded pass per company per interval. It does not
// register a liveness heartbeat, because a business finding or a slow audit
// must never restart the process; a stalled auditor shows as stale coverage.
public sealed class ConsistencyAuditOperation(
  ConsistencyAuditor auditor,
  IServiceScopeFactory scopes,
  IOptions<ConsistencyAuditOptions> options,
  TimeProvider clock,
  ILogger<ConsistencyAuditOperation> logger
) : IConsistencyAuditOperation
{
  public async Task RunAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      if (options.Value.Enabled)
        try
        {
          await using var scope = scopes.CreateAsyncScope();
          await CompanyPasses.ForEachCompanyAsync(
            scope.ServiceProvider,
            async token =>
            {
              if (
                scope.ServiceProvider.GetService<ICurrentCompany>()?.Id is
                { } company
              )
                await auditor.RunAsync(company, token);
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
          logger.LogWarning(ex, "Consistency audit sweep failed");
        }
      try
      {
        await Task.Delay(
          TimeSpan.FromMinutes(options.Value.IntervalMinutes),
          clock,
          ct
        );
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }
}
