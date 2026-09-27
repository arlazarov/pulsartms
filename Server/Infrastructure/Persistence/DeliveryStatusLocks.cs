using Application.Features.Messaging.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DeliveryStatusLocks(AppDbContext db) : IDeliveryStatusLocks
{
  public Task LockAdmissionAsync(Guid company, CancellationToken ct) =>
    LockAsync($"delivery-status-admission:{company:N}", ct);

  public Task LockAsync(
    Guid company,
    string channel,
    string businessNumber,
    string providerMessageId,
    CancellationToken ct
  ) =>
    LockAsync(
      $"delivery-status:{company:N}:{channel}:{businessNumber}:{providerMessageId}",
      ct
    );

  private async Task LockAsync(string key, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "A delivery status lock is held by a transaction."
      );
    // SQLite runs one writer at a time, which orders these already.
    if (!db.Database.IsNpgsql())
      return;
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
      ct
    );
  }
}
