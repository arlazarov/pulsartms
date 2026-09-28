using Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Infrastructure.Diagnostics;

// Stale background progress, reported on readiness as degraded - never as
// unhealthy, and never on liveness: with one instance, restarting it over a
// slow round would be an outage (root's decision). The names are in the
// diagnostics report (api/diagnostics/background).
public sealed class BackgroundProgressHealthCheck(
  IBackgroundState background,
  TimeProvider clock
) : IHealthCheck
{
  public Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context,
    CancellationToken cancellationToken = default
  )
  {
    var stale = background.StaleProgress(clock.GetUtcNow());
    return Task.FromResult(
      stale.Count == 0
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Degraded(
          "Background progress is stale: " + string.Join(", ", stale)
        )
    );
  }
}
