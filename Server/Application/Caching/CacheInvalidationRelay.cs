using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Domain.Entities.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Caching;

// Read caches are per process. An instance that invalidates a group drops its
// own copy and nobody else's, so a second instance answers with reads the
// first one already knows are wrong. This carries invalidations between
// instances: it publishes what this one dropped and applies what the others
// published.
//
// It does not make the caches shared, and it is not instant. Between another
// instance's write and the next poll this one may still answer from cache.
// That window is the price of caching at all; what it replaces is a stale
// answer that never corrects itself.
public sealed class CacheInvalidationRelay(
  ReadCache cache,
  IServiceScopeFactory scopes,
  IOptions<SynchronizationOptions> options,
  TimeProvider clock,
  ILogger<CacheInvalidationRelay> logger
)
{
  // Wide enough to cover any delay between a row being written and becoming
  // visible, and any clock difference between instances, because rows are read
  // by time rather than by a cursor that could move past a late one.
  private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);
  private static readonly TimeSpan Keep = TimeSpan.FromHours(1);
  private static readonly TimeSpan BetweenPrunes = TimeSpan.FromMinutes(10);

  private readonly string instance = Guid.NewGuid().ToString("n");
  private readonly Dictionary<Guid, DateTime> applied = [];
  private DateTime prunedAt = DateTime.MinValue;

  public string Instance => instance;

  public async Task RunAsync(CancellationToken ct)
  {
    var interval = TimeSpan.FromSeconds(
      Math.Clamp(options.Value.CacheRelaySeconds, 1, 60)
    );
    while (!ct.IsCancellationRequested)
    {
      try
      {
        await RunOnceAsync(ct);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        // A failed round leaves this instance's invalidations queued and its
        // caches unchanged, so the next round still carries them.
        logger.LogWarning(
          ex,
          "Cache invalidations were not exchanged this round."
        );
      }
      try
      {
        await Task.Delay(interval, clock, ct);
      }
      catch (OperationCanceledException)
      {
        return;
      }
    }
  }

  public async Task RunOnceAsync(CancellationToken ct)
  {
    await using var scope = scopes.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
    var now = clock.GetUtcNow().UtcDateTime;

    await PublishAsync(db, now, ct);
    await ApplyAsync(db, now, ct);
    await PruneAsync(db, now, ct);
  }

  private async Task PublishAsync(
    IAppDbContext db,
    DateTime now,
    CancellationToken ct
  )
  {
    var pending = cache.Unpublished();
    if (pending.Count == 0)
      return;
    db.CacheInvalidations.AddRange(
      pending.Select(x => new CacheInvalidation
      {
        Id = Guid.NewGuid(),
        GroupKey = x.Key,
        RecordedAt = now,
        RecordedBy = instance,
      })
    );
    await db.SaveChangesAsync(ct);
    cache.Published(pending);
  }

  private async Task ApplyAsync(
    IAppDbContext db,
    DateTime now,
    CancellationToken ct
  )
  {
    var since = now - Window;
    // This instance's own rows are skipped: it dropped those groups when it
    // invalidated them, and applying them again would only cost a reload.
    var rows = await db
      .CacheInvalidations.Where(x =>
        x.RecordedAt >= since && x.RecordedBy != instance
      )
      .Select(x => new
      {
        x.Id,
        x.GroupKey,
        x.RecordedAt,
      })
      .ToListAsync(ct);

    foreach (var row in rows)
      if (applied.TryAdd(row.Id, row.RecordedAt))
        cache.ApplyPublished(row.GroupKey);

    // A row outside the window can no longer arrive, so remembering it no
    // longer prevents anything.
    foreach (
      var id in applied.Where(x => x.Value < since).Select(x => x.Key).ToArray()
    )
      applied.Remove(id);
  }

  // Cached entries expire in minutes, so an invalidation older than an hour
  // can no longer apply to anything still held. Any instance may prune; the
  // delete is idempotent and the interval keeps them from all doing it at
  // once.
  private async Task PruneAsync(
    IAppDbContext db,
    DateTime now,
    CancellationToken ct
  )
  {
    if (now - prunedAt < BetweenPrunes)
      return;
    prunedAt = now;
    var before = now - Keep;
    await db
      .CacheInvalidations.Where(x => x.RecordedAt < before)
      .ExecuteDeleteAsync(ct);
  }
}
