using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Synchronization;

// The synchronization loop's lease, held now by an owner without the
// marker: a binary from before it. Only that one row: other checkpoints
// are leased by other loops and owners.
public sealed class PreviousBinary(AppDbContext db, TimeProvider clock)
  : IPreviousBinary
{
  public Task<bool> RunsAsync(CancellationToken ct)
  {
    var now = clock.GetUtcNow().UtcDateTime;
    return db
      .SynchronizationCheckpoints.AsNoTracking()
      .AnyAsync(
        x =>
          x.Id == SynchronizationStore.Id
          && x.Owner != ""
          && x.LeaseUntil > now
          && !x.Owner.StartsWith(IPreviousBinary.Marker),
        ct
      );
  }
}
