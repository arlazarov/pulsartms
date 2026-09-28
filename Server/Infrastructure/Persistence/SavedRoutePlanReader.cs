using System.Text.Json;
using Application.Features.Routing.Interfaces;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

public sealed class SavedRoutePlanReader(
  AppDbContext db,
  ILogger<SavedRoutePlanReader> logger
) : ISavedRoutePlanReader
{
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );

  public async Task<SavedRoutePlanMetadata?> ReadAsync(
    Guid dispatchId,
    CancellationToken ct
  )
  {
    var json = await Metadata(dispatchId).SingleOrDefaultAsync(ct);
    if (json is null)
      return null;
    try
    {
      var value = JsonSerializer.Deserialize<SavedRoutePlanMetadata>(
        json,
        Json
      );
      return
        value?.Tracking is { PassedStopIds: not null, VisitedStops: not null }
        ? value
        : null;
    }
    catch (JsonException ex)
    {
      logger.LogWarning(ex, "Discarding an unreadable saved route plan");
      return null;
    }
  }

  private IQueryable<string> Metadata(Guid dispatchId) =>
    MetadataRows([dispatchId], []).Select(x => x.Value);

  public async Task<SavedRoutePlanMetadata?> ReadExecutionLegAsync(
    Guid executionLegId,
    CancellationToken ct
  ) =>
    (await ReadExecutionLegsAsync([executionLegId], ct)).GetValueOrDefault(
      executionLegId
    );

  public async Task<
    IReadOnlyDictionary<Guid, SavedRoutePlanMetadata>
  > ReadExecutionLegsAsync(
    IReadOnlyCollection<Guid> executionLegIds,
    CancellationToken ct
  ) => (await ReadWorkAsync([], executionLegIds, ct)).Legs;

  public async Task<
    IReadOnlyDictionary<Guid, SavedRoutePlanMetadata>
  > ReadManyAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    CancellationToken ct
  ) => (await ReadWorkAsync(dispatchIds, [], ct)).Loads;

  public async Task<SavedRoutePlanMetadataSet> ReadWorkAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    IReadOnlyCollection<Guid> executionLegIds,
    CancellationToken ct
  )
  {
    var loads = new Dictionary<Guid, SavedRoutePlanMetadata>();
    var legs = new Dictionary<Guid, SavedRoutePlanMetadata>();
    if (dispatchIds.Count == 0 && executionLegIds.Count == 0)
      return new(loads, legs);
    var rows = await MetadataRows(
        dispatchIds.ToArray(),
        executionLegIds.ToArray()
      )
      .ToListAsync(ct);
    foreach (var row in rows)
    {
      try
      {
        var value = JsonSerializer.Deserialize<SavedRoutePlanMetadata>(
          row.Value,
          Json
        );
        if (
          value?.Tracking is { PassedStopIds: not null, VisitedStops: not null }
        )
          if (row.ExecutionLegId is { } leg)
            legs[leg] = value;
          else
            loads[row.DispatchId] = value;
      }
      catch (JsonException) { }
    }
    return new(loads, legs);
  }

  private sealed class MetadataRow
  {
    public Guid DispatchId { get; set; }
    public Guid? ExecutionLegId { get; set; }
    public string Value { get; set; } = "";
  }

  private IQueryable<MetadataRow> MetadataRows(Guid[] loads, Guid[] legs)
  {
    // JSON stays inside the database; saved-road validation must not
    // transfer route geometry. Raw SQL skips the company filter, so the
    // serving carrier is named here: ids alone must not reach another
    // carrier's plans. Nobody identified (null) matches no row, as the
    // filter does.
    var company = db.ServingCompany;
    if (db.Database.IsNpgsql())
      return db.Database.SqlQuery<MetadataRow>(
        $$"""
        WITH selected AS MATERIALIZED (
          SELECT "DispatchId", "ExecutionLegId", "InputHash", "TruckId",
            "AssignmentRevision", "PlanJson"::jsonb AS document
          FROM "DispatchRoutePlans"
          WHERE "CompanyId" = {{company}}
            AND ("ExecutionLegId" = ANY({{legs}})
              OR ("ExecutionLegId" IS NULL AND "DispatchId" = ANY({{loads}})))
        )
        SELECT "DispatchId", "ExecutionLegId", jsonb_build_object(
          'inputHash', "InputHash", 'truckId', "TruckId",
          'dispatchId', "DispatchId",
          'executionLegId', "ExecutionLegId",
          'storedAssignmentRevision', "AssignmentRevision",
          'planDispatchId', document -> 'dispatchId',
          'fromCurrentPosition', document -> 'fromCurrentPosition',
          'planExecutionLegId', document -> 'executionLegId',
          'assignmentRevision', COALESCE(document -> 'assignmentRevision', '0'),
          'planTruckId', document -> 'truckId', 'planId', document -> 'id',
          'version', document -> 'version',
          'tracking', COALESCE(document -> 'tracking', '{}'::jsonb),
          'fuelCalculatedAt', document #> '{fuelPlan,calculatedAt}')
          ::text AS "Value"
        FROM selected
        """
      );
    if (db.Database.IsSqlite())
      return db
        .Database.SqlQuery<MetadataRow>(
          $$"""
          SELECT "DispatchId", "ExecutionLegId", json_object(
            'inputHash', "InputHash", 'truckId', "TruckId",
            'dispatchId', "DispatchId",
            'executionLegId', "ExecutionLegId",
            'storedAssignmentRevision', "AssignmentRevision",
            'planDispatchId', json_extract("PlanJson", '$.dispatchId'),
            'fromCurrentPosition', json(CASE
              WHEN json_extract("PlanJson", '$.fromCurrentPosition')
              THEN 'true' ELSE 'false' END),
            'planExecutionLegId', json_extract("PlanJson", '$.executionLegId'),
            'assignmentRevision', COALESCE(
              json_extract("PlanJson", '$.assignmentRevision'), 0),
            'planTruckId', json_extract("PlanJson", '$.truckId'),
            'planId', json_extract("PlanJson", '$.id'),
            'version', json_extract("PlanJson", '$.version'),
            'tracking', json(COALESCE(
              json_extract("PlanJson", '$.tracking'), '{}')),
            'fuelCalculatedAt',
              json_extract("PlanJson", '$.fuelPlan.calculatedAt')) AS "Value"
          FROM "DispatchRoutePlans"
          WHERE "CompanyId" = {{company}}
          """
        )
        .Where(x =>
          x.ExecutionLegId != null && legs.Contains(x.ExecutionLegId.Value)
          || x.ExecutionLegId == null && loads.Contains(x.DispatchId)
        );
    throw new NotSupportedException(
      "Saved route metadata requires PostgreSQL or SQLite."
    );
  }
}
