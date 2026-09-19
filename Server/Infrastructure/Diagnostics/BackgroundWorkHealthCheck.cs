using Application.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Infrastructure.Diagnostics;

// Liveness that means something. Before this, /api/health/live ran no checks
// at all and answered "alive" for a process whose background work had
// stopped - which is exactly what happened, unnoticed, for seven and a half
// hours.
//
// Reporting unhealthy here tells the platform to replace the container, so
// the usual case repairs itself without anybody being woken up.
public sealed class BackgroundWorkHealthCheck(TimeProvider clock) : IHealthCheck
{
  public Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context,
    CancellationToken cancellationToken = default
  )
  {
    var stalled = BackgroundHeartbeat.Stalled(clock.GetUtcNow());
    return Task.FromResult(
      stalled.Count == 0
        ? HealthCheckResult.Healthy()
        : HealthCheckResult.Unhealthy(
          "Background work stopped: " + string.Join(", ", stalled)
        )
    );
  }
}
