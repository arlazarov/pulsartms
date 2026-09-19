using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Persistence;

public sealed class DatabaseInitializer(
  IServiceScopeFactory scopes,
  IConfiguration configuration
) : IHostedService
{
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    if (!configuration.GetValue<bool>("Database:ApplyMigrations"))
      return;
    await using var scope = scopes.CreateAsyncScope();
    await scope
      .ServiceProvider.GetRequiredService<AppDbContext>()
      .Database.MigrateAsync(cancellationToken);
  }

  public Task StopAsync(CancellationToken cancellationToken) =>
    Task.CompletedTask;
}
