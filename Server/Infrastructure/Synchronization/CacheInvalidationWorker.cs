using Application.Caching;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Synchronization;

// Deliberately not an ApplicationWorker: BackgroundOperations:Enabled and
// :Roles decide which instance does which work, and carrying invalidations is
// not work that can be given to one instance. An instance that serves reads
// needs it, and an instance configured to serve reads and nothing else is
// exactly the one that would otherwise answer from caches nobody can clear.
public sealed class CacheInvalidationWorker(CacheInvalidationRelay relay)
  : BackgroundService
{
  protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
    relay.RunAsync(stoppingToken);
}
