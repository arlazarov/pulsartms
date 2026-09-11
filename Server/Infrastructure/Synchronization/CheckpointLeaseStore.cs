using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Synchronization;

internal sealed class CheckpointLeaseStore(AppDbContext db, Guid id)
{
  public async Task<bool> AcquireAsync(string owner, DateTime now, CancellationToken ct)
  {
    if (!await db.SynchronizationCheckpoints.AnyAsync(x => x.Id == id, ct))
    {
      var checkpoint = new Domain.Entities.Fleet.SynchronizationCheckpoint { Id = id, LeaseUntil = now, UpdatedAt = now };
      db.SynchronizationCheckpoints.Add(checkpoint);
      try { await db.SaveChangesAsync(ct); }
      catch (DbUpdateException) { db.Entry(checkpoint).State = EntityState.Detached; }
    }
    return await db.SynchronizationCheckpoints.Where(x => x.Id == id && (x.LeaseUntil <= now || x.Owner == owner))
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Owner, owner).SetProperty(x => x.LeaseUntil, now.AddMinutes(3)), ct) == 1;
  }

  public async Task<bool> RenewAsync(string owner, DateTime now, CancellationToken ct) =>
    await db.SynchronizationCheckpoints.Where(x => x.Id == id && x.Owner == owner && x.LeaseUntil > now)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, now.AddMinutes(3)), ct) == 1;

  public Task<string?> ReadAsync(CancellationToken ct) => db.SynchronizationCheckpoints
    .Where(x => x.Id == id).Select(x => x.StateJson).SingleOrDefaultAsync(ct);

  public async Task SaveAsync(string owner, string json, CancellationToken ct)
  {
    var now = DateTime.UtcNow;
    var changed = await db.SynchronizationCheckpoints.Where(x => x.Id == id && x.Owner == owner && x.LeaseUntil > now)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.StateJson, json).SetProperty(x => x.UpdatedAt, now), ct);
    if (changed != 1) throw new InvalidOperationException("Background checkpoint lease was lost.");
  }

  public Task ReleaseAsync(string owner, CancellationToken ct) => db.SynchronizationCheckpoints
    .Where(x => x.Id == id && x.Owner == owner)
    .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, DateTime.UtcNow).SetProperty(x => x.Owner, ""), ct);
}
