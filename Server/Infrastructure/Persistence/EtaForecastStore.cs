using System.Text.Json;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class EtaForecastStore(AppDbContext db) : IEtaForecastStore
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task<IReadOnlyList<EtaForecastSnapshot>> ReadAsync(IReadOnlyCollection<Guid> dispatchIds, CancellationToken ct)
  {
    if (dispatchIds.Count == 0) return [];
    var ids = dispatchIds.Distinct().ToArray();
    var rows = await db.Set<DispatchEtaForecast>().AsNoTracking().Where(x => ids.Contains(x.DispatchId)).ToListAsync(ct);
    var result = new List<EtaForecastSnapshot>(rows.Count);
    foreach (var row in rows)
    {
      DispatchEta? forecast;
      try { forecast = JsonSerializer.Deserialize<DispatchEta>(row.ForecastJson, Json); }
      catch (JsonException) { continue; }
      if (forecast?.Stops is null || forecast.Assumptions is null || DatabaseInstant(forecast.CalculatedAt) != row.CalculatedAt
        || DatabaseInstant(forecast.ValidUntil) != row.ValidUntil) continue;
      result.Add(new(row.DispatchId, row.TruckId, row.RootDispatchId, row.InputHash, row.DriverExternalId, forecast));
    }
    return result;
  }

  public async Task<bool> SaveAsync(IReadOnlyCollection<EtaForecastSnapshot> snapshots, CancellationToken ct)
  {
    if (snapshots.Count == 0) return true;
    if (snapshots.Select(x => x.DispatchId).Distinct().Count() != snapshots.Count)
      throw new ArgumentException("A forecast batch must contain each dispatch once.", nameof(snapshots));
    var outer = db.Database.CurrentTransaction;
    await using var owned = outer is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var transaction = outer ?? owned!;
    var savepoint = "eta_" + Guid.NewGuid().ToString("N");
    if (outer is not null) await transaction.CreateSavepointAsync(savepoint, ct);
    try
    {
      foreach (var snapshot in snapshots.OrderBy(x => x.DispatchId))
      {
        // Keep the original calculation identity in JSON; PostgreSQL row timestamps use microseconds.
        var forecast = snapshot.Forecast;
        var json = JsonSerializer.Serialize(forecast, Json);
        // Conditional upsert prevents an older concurrent chain from replacing a newer snapshot.
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
          INSERT INTO "DispatchEtaForecasts"
            ("Id", "DispatchId", "TruckId", "RootDispatchId", "InputHash", "DriverExternalId", "CalculatedAt", "ValidUntil", "ForecastJson")
          VALUES ({snapshot.DispatchId}, {snapshot.DispatchId}, {snapshot.TruckId}, {snapshot.RootDispatchId},
            {snapshot.InputHash}, {snapshot.DriverExternalId}, {DatabaseInstant(forecast.CalculatedAt)}, {DatabaseInstant(forecast.ValidUntil)}, {json})
          ON CONFLICT ("DispatchId") DO UPDATE SET
            "TruckId" = excluded."TruckId", "RootDispatchId" = excluded."RootDispatchId",
            "InputHash" = excluded."InputHash", "DriverExternalId" = excluded."DriverExternalId",
            "CalculatedAt" = excluded."CalculatedAt", "ValidUntil" = excluded."ValidUntil", "ForecastJson" = excluded."ForecastJson"
          WHERE "DispatchEtaForecasts"."CalculatedAt" < excluded."CalculatedAt"
          """, ct);
        if (changed == 0)
        {
          if (outer is not null)
          {
            await transaction.RollbackToSavepointAsync(savepoint, ct);
            await transaction.ReleaseSavepointAsync(savepoint, ct);
          }
          else await transaction.RollbackAsync(ct);
          return false;
        }
      }
      ct.ThrowIfCancellationRequested();
      if (outer is not null) await transaction.ReleaseSavepointAsync(savepoint, ct);
      else await transaction.CommitAsync(ct);
      return true;
    }
    catch
    {
      if (outer is not null) await transaction.RollbackToSavepointAsync(savepoint, CancellationToken.None);
      throw;
    }
  }

  private static DateTime DatabaseInstant(DateTime value) => new(value.ToUniversalTime().Ticks / 10 * 10, DateTimeKind.Utc);
}
