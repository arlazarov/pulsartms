using Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Synchronization;

public sealed class ApplicationWorker<TOperation>(
  TOperation operation,
  IConfiguration configuration
) : BackgroundService
  where TOperation : class, IBackgroundOperation
{
  protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
    configuration.GetValue("BackgroundOperations:Enabled", true)
      ? operation.RunAsync(stoppingToken)
      : Task.CompletedTask;
}
