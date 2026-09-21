using Application.Features.Fleet.Interfaces;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DriverHosStore(IAppDbContext db) : IDriverHosStore
{
  // Hours decide whether a driver may legally drive, so a reading is only
  // worth answering with while it is recent. The in-memory snapshot keeps a
  // minute because a refresh is running beside it; an instance reading what
  // another recorded needs longer, but not unbounded. Fifteen minutes matches
  // the bound the board already applies, so nothing is handed out that a
  // reader would be right to reject.
  public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(15);

  public async Task<IReadOnlyDictionary<string, DriverHosClocks>> ReadAsync(
    CancellationToken ct
  )
  {
    var since = DateTime.UtcNow - Freshness;
    return await db
      .DriverHosReadings.AsNoTracking()
      .Where(x => x.ObservedAt > since)
      .ToDictionaryAsync(
        x => x.DriverExternalId,
        x => new DriverHosClocks
        {
          BreakMs = x.BreakMs,
          DriveMs = x.DriveMs,
          ShiftMs = x.ShiftMs,
          CycleMs = x.CycleMs,
          UpdatedAt = x.ObservedAt,
          CurrentDutyStatus = x.CurrentDutyStatus,
        },
        StringComparer.Ordinal,
        ct
      );
  }

  // The provider returns the complete set each time, so the stored set is
  // replaced rather than merged: a driver the provider stopped reporting must
  // not keep an old reading alive.
  public async Task WriteAsync(
    IReadOnlyDictionary<string, DriverHosClocks> clocks,
    CancellationToken ct
  )
  {
    var now = DateTime.UtcNow;
    var existing = await db.DriverHosReadings.ToListAsync(ct);
    db.DriverHosReadings.RemoveRange(
      existing.Where(x => !clocks.ContainsKey(x.DriverExternalId))
    );
    var byId = existing.ToDictionary(
      x => x.DriverExternalId,
      StringComparer.Ordinal
    );
    foreach (var (id, value) in clocks)
    {
      if (!byId.TryGetValue(id, out var row))
      {
        row = new DriverHosReading { DriverExternalId = id };
        db.DriverHosReadings.Add(row);
      }
      row.BreakMs = value.BreakMs;
      row.DriveMs = value.DriveMs;
      row.ShiftMs = value.ShiftMs;
      row.CycleMs = value.CycleMs;
      row.CurrentDutyStatus = value.CurrentDutyStatus;
      row.ObservedAt = value.UpdatedAt;
      row.RecordedAt = now;
    }
    await db.SaveChangesAsync(ct);
  }
}
