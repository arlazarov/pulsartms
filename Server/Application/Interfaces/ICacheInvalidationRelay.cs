namespace Application.Interfaces;

// Carries read-cache invalidations between instances for as long as the
// process runs. Every instance that serves reads runs it, whatever work it
// is given; see CacheInvalidationRelay.
public interface ICacheInvalidationRelay
{
  Task RunAsync(CancellationToken ct);
}
