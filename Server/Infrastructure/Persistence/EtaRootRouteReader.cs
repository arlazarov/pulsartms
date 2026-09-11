using System.Text.Json;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class EtaRootRouteReader(AppDbContext db) : IEtaRootRouteReader
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public async Task<EtaRootRouteMetadata?> ReadAsync(Guid dispatchId, CancellationToken ct)
  {
    var json = await Metadata(dispatchId).SingleOrDefaultAsync(ct);
    if (json is null) return null;
    try
    {
      var value = JsonSerializer.Deserialize<EtaRootRouteMetadata>(json, Json);
      return value?.Tracking is { PassedStopIds: not null, VisitedStops: not null } ? value : null;
    }
    catch (JsonException) { return null; }
  }

  private IQueryable<string> Metadata(Guid dispatchId)
  {
    // JSON stays inside the database; saved forecast validation must not transfer route geometry.
    if (db.Database.IsNpgsql())
      return db.Database.SqlQuery<string>($$"""
        SELECT jsonb_build_object(
          'inputHash', "InputHash", 'truckId', "TruckId",
          'planTruckId', "PlanJson"::jsonb -> 'truckId', 'planId', "PlanJson"::jsonb -> 'id',
          'version', "PlanJson"::jsonb -> 'version',
          'tracking', COALESCE("PlanJson"::jsonb -> 'tracking', '{}'::jsonb),
          'fuelCalculatedAt', "PlanJson"::jsonb #> '{fuelPlan,calculatedAt}')::text AS "Value"
        FROM "DispatchRoutePlans" WHERE "DispatchId" = {{dispatchId}}
        """);
    if (db.Database.IsSqlite())
      return db.Database.SqlQuery<string>($$"""
        SELECT json_object(
          'inputHash', "InputHash", 'truckId', "TruckId",
          'planTruckId', json_extract("PlanJson", '$.truckId'), 'planId', json_extract("PlanJson", '$.id'),
          'version', json_extract("PlanJson", '$.version'),
          'tracking', json(COALESCE(json_extract("PlanJson", '$.tracking'), '{}')),
          'fuelCalculatedAt', json_extract("PlanJson", '$.fuelPlan.calculatedAt')) AS "Value"
        FROM "DispatchRoutePlans" WHERE "DispatchId" = {{dispatchId}}
        """);
    throw new NotSupportedException("Saved ETA route metadata requires PostgreSQL or SQLite.");
  }
}
