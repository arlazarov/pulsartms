using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class DriverHosStore(IAppDbContext db) : IDriverHosStore
{
  public async Task<IReadOnlyDictionary<string, DriverHosClocks>> ReadAsync(
    CancellationToken ct
  ) =>
    await db
      .DriverHosReadings.AsNoTracking()
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
