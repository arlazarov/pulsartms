using System.Text.Json;
using Application.Features.Eta.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Models.Eta;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class EtaForecastStore(AppDbContext db) : IEtaForecastStore
{
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public async Task<IReadOnlyList<EtaForecastSnapshot>> ReadAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    CancellationToken ct
  )
  {
    if (dispatchIds.Count == 0)
      return [];
    var ids = dispatchIds.Distinct().ToArray();
    var rows = await db.Set<DispatchEtaForecast>()
      .AsNoTracking()
      .Where(x => x.ExecutionLegId == null && ids.Contains(x.DispatchId))
      .ToListAsync(ct);
    return ReadRows(rows);
  }

  public async Task<IReadOnlyList<EtaForecastSnapshot>> ReadExecutionLegsAsync(
    IReadOnlyCollection<Guid> executionLegIds,
    CancellationToken ct
  )
  {
    if (executionLegIds.Count == 0)
      return [];
    var ids = executionLegIds.Distinct().ToArray();
    var rows = await db.Set<DispatchEtaForecast>()
      .AsNoTracking()
      .Where(x =>
        x.ExecutionLegId != null && ids.Contains(x.ExecutionLegId.Value)
      )
      .ToListAsync(ct);
    return ReadRows(rows);
  }

  private static IReadOnlyList<EtaForecastSnapshot> ReadRows(
    List<DispatchEtaForecast> rows
  )
  {
    var result = new List<EtaForecastSnapshot>(rows.Count);
    foreach (var row in rows)
    {
      DispatchEta? forecast;
      try
      {
        forecast = JsonSerializer.Deserialize<DispatchEta>(
          row.ForecastJson,
          Json
        );
      }
      catch (JsonException)
      {
        continue;
      }
      if (
        forecast?.Stops is null
        || forecast.Assumptions is null
        || DatabaseInstant(forecast.CalculatedAt) != row.CalculatedAt
        || DatabaseInstant(forecast.ValidUntil) != row.ValidUntil
      )
        continue;
      result.Add(
        new(
          row.DispatchId,
          row.TruckId,
          row.RootDispatchId,
          row.InputHash,
          row.DriverExternalId,
          forecast
        )
        {
          ExecutionLegId = row.ExecutionLegId,
          RootExecutionLegId = row.RootExecutionLegId,
          AssignmentRevision = row.AssignmentRevision,
        }
      );
    }
    return result;
  }

  public async Task<bool> SaveAsync(
    IReadOnlyCollection<EtaForecastSnapshot> snapshots,
    CancellationToken ct
  )
  {
    if (snapshots.Count == 0)
      return true;
    if (
      snapshots
        .Select(x =>
          (x.ExecutionLegId.HasValue, x.ExecutionLegId ?? x.DispatchId)
        )
        .Distinct()
        .Count() != snapshots.Count
    )
      throw new ArgumentException(
        "A forecast batch must contain each planning scope once.",
        nameof(snapshots)
      );
    var outer = db.Database.CurrentTransaction;
    await using var owned = outer is null
      ? await db.Database.BeginTransactionAsync(ct)
      : null;
    var transaction = outer ?? owned!;
    var savepoint = "eta_" + Guid.NewGuid().ToString("N");
    if (outer is not null)
      await transaction.CreateSavepointAsync(savepoint, ct);
    try
    {
      foreach (
        var snapshot in snapshots
          .Where(x => x.ExecutionLegId.HasValue)
          .OrderBy(x => x.ExecutionLegId)
      )
      {
        if (
          await db.LockExecutionLegAsync(
            snapshot.ExecutionLegId!.Value,
            snapshot.AssignmentRevision,
            ct
          )
        )
          continue;
        if (outer is not null)
        {
          await transaction.RollbackToSavepointAsync(savepoint, ct);
          await transaction.ReleaseSavepointAsync(savepoint, ct);
        }
        else
          await transaction.RollbackAsync(ct);
        return false;
      }
      foreach (
        var snapshot in snapshots
          .OrderBy(x => x.DispatchId)
          .ThenBy(x => x.ExecutionLegId)
      )
      {
        // Keep the original calculation identity in JSON; PostgreSQL row
        // timestamps use microseconds.
        var forecast = snapshot.Forecast;
        var json = JsonSerializer.Serialize(forecast, Json);
        // Conditional upsert prevents an older concurrent chain from replacing
        // a newer snapshot.
        var changed = await UpsertAsync(snapshot, json, ct);
        if (changed == 0)
        {
          if (outer is not null)
          {
            await transaction.RollbackToSavepointAsync(savepoint, ct);
            await transaction.ReleaseSavepointAsync(savepoint, ct);
          }
          else
            await transaction.RollbackAsync(ct);
          return false;
        }
      }
      ct.ThrowIfCancellationRequested();
      if (outer is not null)
        await transaction.ReleaseSavepointAsync(savepoint, ct);
      else
        await transaction.CommitAsync(ct);
      return true;
    }
    catch
    {
      if (outer is not null)
        await transaction.RollbackToSavepointAsync(
          savepoint,
          CancellationToken.None
        );
      throw;
    }
  }

  private Task<int> UpsertAsync(
    EtaForecastSnapshot value,
    string json,
    CancellationToken ct
  )
  {
    var id = Guid.NewGuid();
    var calculatedAt = DatabaseInstant(value.Forecast.CalculatedAt);
    var validUntil = DatabaseInstant(value.Forecast.ValidUntil);
    if (value.ExecutionLegId is not null)
      return db.Database.ExecuteSqlInterpolatedAsync(
        $"""
        INSERT INTO "DispatchEtaForecasts"
          ("Id", "DispatchId", "ExecutionLegId", "AssignmentRevision",
           "TruckId",
           "RootDispatchId", "RootExecutionLegId", "InputHash",
           "DriverExternalId", "CalculatedAt", "ValidUntil", "ForecastJson")
        VALUES ({id}, {value.DispatchId}, {value.ExecutionLegId},
          {value.AssignmentRevision},
          {value.TruckId}, {value.RootDispatchId}, {value.RootExecutionLegId},
          {value.InputHash}, {value.DriverExternalId}, {calculatedAt},
          {validUntil}, {json})
        ON CONFLICT ("ExecutionLegId")
          WHERE "ExecutionLegId" IS NOT NULL DO UPDATE SET
          "TruckId" = excluded."TruckId",
          "RootDispatchId" = excluded."RootDispatchId",
          "RootExecutionLegId" = excluded."RootExecutionLegId",
          "AssignmentRevision" = excluded."AssignmentRevision",
          "InputHash" = excluded."InputHash",
          "DriverExternalId" = excluded."DriverExternalId",
          "CalculatedAt" = excluded."CalculatedAt",
          "ValidUntil" = excluded."ValidUntil",
          "ForecastJson" = excluded."ForecastJson"
        WHERE "DispatchEtaForecasts"."CalculatedAt" < excluded."CalculatedAt"
          AND "DispatchEtaForecasts"."DispatchId" = excluded."DispatchId"
        """,
        ct
      );
    return db.Database.ExecuteSqlInterpolatedAsync(
      $"""
      INSERT INTO "DispatchEtaForecasts"
        ("Id", "DispatchId", "AssignmentRevision", "TruckId",
         "RootDispatchId", "InputHash",
         "DriverExternalId", "CalculatedAt", "ValidUntil", "ForecastJson")
      VALUES ({id}, {value.DispatchId}, 0, {value.TruckId}, {value.RootDispatchId},
        {value.InputHash}, {value.DriverExternalId}, {calculatedAt},
        {validUntil}, {json})
      ON CONFLICT ("DispatchId") WHERE "ExecutionLegId" IS NULL DO UPDATE SET
        "TruckId" = excluded."TruckId",
        "RootDispatchId" = excluded."RootDispatchId",
        "AssignmentRevision" = 0,
        "InputHash" = excluded."InputHash",
        "DriverExternalId" = excluded."DriverExternalId",
        "CalculatedAt" = excluded."CalculatedAt",
        "ValidUntil" = excluded."ValidUntil",
        "ForecastJson" = excluded."ForecastJson"
      WHERE "DispatchEtaForecasts"."CalculatedAt" < excluded."CalculatedAt"
      """,
      ct
    );
  }

  private static DateTime DatabaseInstant(DateTime value) =>
    new(value.ToUniversalTime().Ticks / 10 * 10, DateTimeKind.Utc);
}
