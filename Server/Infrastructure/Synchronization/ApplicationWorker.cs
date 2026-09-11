using Application.Features.Synchronization.Interfaces;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Synchronization;

public sealed class ApplicationWorker<TOperation>(TOperation operation) : BackgroundService
  where TOperation : class, IBackgroundOperation
{
  protected override Task ExecuteAsync(CancellationToken stoppingToken) => operation.RunAsync(stoppingToken);
}
