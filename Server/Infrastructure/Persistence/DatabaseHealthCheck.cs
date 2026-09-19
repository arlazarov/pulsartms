using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Infrastructure.Persistence;

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopes)
  : IHealthCheck
{
  public async Task<HealthCheckResult> CheckHealthAsync(
    HealthCheckContext context,
    CancellationToken cancellationToken = default
  )
  {
    await using var scope = scopes.CreateAsyncScope();
    return await scope
      .ServiceProvider.GetRequiredService<AppDbContext>()
      .Database.CanConnectAsync(cancellationToken)
      ? HealthCheckResult.Healthy()
      : HealthCheckResult.Unhealthy("Database unavailable");
  }
}
