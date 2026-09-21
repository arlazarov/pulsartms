using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Application.Features.Routing.Interfaces;
using Domain.Entities.Fuel;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Persistence;

public sealed class TruckFuelPlanStore(
  AppDbContext db,
  ILogger<TruckFuelPlanStore> logger
) : ITruckFuelPlanStore
{
  private const int MaximumSummaryBytes = 512 * 1024;
  private const int MaximumRouteBytes = 8 * 1024 * 1024;
  private static readonly JsonSerializerOptions Json = CreateJson();

  public async Task<TruckFuelPlanSnapshot?> ReadAsync(
    Guid truckId,
    bool includeRoute,
    CancellationToken ct
  )
  {
    var query = db.Set<TruckFuelPlan>()
      .AsNoTracking()
      .Where(x => x.TruckId == truckId);
    var row = includeRoute
      ? await query
        .Select(x => new StoredSnapshot(
          x.TruckId,
          x.RootDispatchId,
          x.CalculatedAt,
          x.SummaryJson,
          x.CheckedRouteJson
        ))
        .SingleOrDefaultAsync(ct)
      : await query
        .Select(x => new StoredSnapshot(
          x.TruckId,
          x.RootDispatchId,
          x.CalculatedAt,
          x.SummaryJson,
          null
        ))
        .SingleOrDefaultAsync(ct);
    if (
      row is null
      || !WithinBytes(row.SummaryJson, MaximumSummaryBytes)
      || row.CheckedRouteJson is { } rawRoute
        && !WithinBytes(rawRoute, MaximumRouteBytes)
    )
      return null;
    try
    {
      var snapshot = JsonSerializer.Deserialize<TruckFuelPlanSnapshot>(
        row.SummaryJson,
        Json
      );
      if (
        snapshot is null
        || snapshot.CheckedRoute is not null
        || snapshot.BaselineRoute is not null
        || !FuelPlanIntegrity.SummaryHolds(snapshot)
        || snapshot.TruckId != row.TruckId
        || snapshot.RootDispatchId != row.RootDispatchId
        || DatabaseInstant(snapshot.CalculatedAt) != row.CalculatedAt
      )
        return null;
      var geometry = row.CheckedRouteJson is null
        ? null
        : JsonSerializer.Deserialize<RouteEnvelope>(row.CheckedRouteJson, Json);
      if (
        row.CheckedRouteJson is not null
        && (
          geometry is null
          || geometry.Checked is null && geometry.Baseline is null
        )
      )
        return null;
      var result = snapshot with
      {
        CheckedRoute = geometry?.Checked,
        BaselineRoute = geometry?.Baseline,
      };
      if (
        includeRoute
        && result.Plan.EstimatedStationAccess
        && result.BaselineRoute is null
      )
        return null;
      return FuelPlanIntegrity.RoutesHold(result) ? result : null;
    }
    catch (JsonException ex)
    {
      logger.LogWarning(
        ex,
        "Discarding an unreadable saved fuel plan for truck {Truck}",
        truckId
      );
      return null;
    }
  }

  public async Task<bool> SaveAsync(
    TruckFuelPlanSnapshot snapshot,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    var (summary, checkedRoute) = Serialize(snapshot);
    // One conditional statement protects the complete result from stale
    // concurrent writers.
    return await db.Database.ExecuteSqlInterpolatedAsync(
        $"""
                INSERT INTO "TruckFuelPlans" ("Id", "TruckId", "CompanyId", "RootDispatchId", "CalculatedAt", "SummaryJson", "CheckedRouteJson")
                VALUES ({snapshot.TruckId}, {snapshot.TruckId}, {Company()}, {snapshot.RootDispatchId}, {DatabaseInstant(
                    snapshot.CalculatedAt
                )}, {summary}, {checkedRoute})
                ON CONFLICT ("TruckId") DO UPDATE SET
                  "RootDispatchId" = excluded."RootDispatchId", "CalculatedAt" = excluded."CalculatedAt",
                  "SummaryJson" = excluded."SummaryJson", "CheckedRouteJson" = excluded."CheckedRouteJson"
                WHERE "TruckFuelPlans"."CalculatedAt" < excluded."CalculatedAt"
                """,
        ct
      ) > 0;
  }

  public async Task<bool> ReplaceAsync(
    TruckFuelPlanSnapshot snapshot,
    DateTime? expectedCalculatedAt,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    if (
      expectedCalculatedAt is { } expected
      && (expected.Kind != DateTimeKind.Utc || expected == default)
    )
      throw new ArgumentException(
        "The expected fuel plan revision must be a non-default UTC instant.",
        nameof(expectedCalculatedAt)
      );
    var (summary, checkedRoute) = Serialize(snapshot);
    var calculatedAt = DatabaseInstant(snapshot.CalculatedAt);
    if (expectedCalculatedAt is not { } revision)
      return await db.Database.ExecuteSqlInterpolatedAsync(
          $"""
          INSERT INTO "TruckFuelPlans" ("Id", "TruckId", "CompanyId", "RootDispatchId", "CalculatedAt", "SummaryJson", "CheckedRouteJson")
          VALUES ({snapshot.TruckId}, {snapshot.TruckId}, {Company()}, {snapshot.RootDispatchId}, {calculatedAt}, {summary}, {checkedRoute})
          ON CONFLICT ("TruckId") DO NOTHING
          """,
          ct
        ) > 0;

    // A later calculation timestamp cannot authorize overwriting a plan changed
    // since the edit began.
    return await db.Database.ExecuteSqlInterpolatedAsync(
        $"""
                UPDATE "TruckFuelPlans"
                SET "RootDispatchId" = {snapshot.RootDispatchId}, "CalculatedAt" = {calculatedAt},
                  "SummaryJson" = {summary}, "CheckedRouteJson" = {checkedRoute}
                WHERE "TruckId" = {snapshot.TruckId} AND "CompanyId" = {Company()}
                  AND "CalculatedAt" = {DatabaseInstant(
                    revision
                )}
                  AND "CalculatedAt" < {calculatedAt}
                """,
        ct
      ) > 0;
  }

  // Raw statements go around the filter and the stamp, so they name the
  // carrier themselves. Writing without one is not a row with a missing
  // field - it is a row that belongs to nobody, and it is refused.
  private Guid Company() =>
    db.ServingCompany
    ?? throw new InvalidOperationException(
      "A truck fuel plan cannot be written without a company."
    );

  private static (string Summary, string? CheckedRoute) Serialize(
    TruckFuelPlanSnapshot snapshot
  )
  {
    if (
      !FuelPlanIntegrity.SummaryHolds(snapshot)
      || !FuelPlanIntegrity.RoutesHold(snapshot)
      || snapshot.Plan.EstimatedStationAccess && snapshot.BaselineRoute is null
    )
      throw new ArgumentException(
        "The truck fuel snapshot is incomplete or exceeds the supported itinerary bounds.",
        nameof(snapshot)
      );
    var summary = JsonSerializer.Serialize(
      snapshot with
      {
        CheckedRoute = null,
        BaselineRoute = null,
      },
      Json
    );
    var checkedRoute =
      snapshot.CheckedRoute is null && snapshot.BaselineRoute is null
        ? null
        : JsonSerializer.Serialize(
          new RouteEnvelope(snapshot.CheckedRoute, snapshot.BaselineRoute),
          Json
        );
    if (
      !WithinBytes(summary, MaximumSummaryBytes)
      || checkedRoute is not null
        && !WithinBytes(checkedRoute, MaximumRouteBytes)
    )
      throw new ArgumentException(
        "The truck fuel snapshot exceeds the supported payload size.",
        nameof(snapshot)
      );
    return (summary, checkedRoute);
  }

  private static bool WithinBytes(string value, int maximum) =>
    value.Length <= maximum && Encoding.UTF8.GetByteCount(value) <= maximum;

  private static DateTime DatabaseInstant(DateTime value) =>
    new(value.Ticks / 10 * 10, DateTimeKind.Utc);

  private static JsonSerializerOptions CreateJson()
  {
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(info =>
    {
      if (info.Type != typeof(TruckRoute))
        return;
      foreach (var property in info.Properties.Where(x => x.Name == "points"))
        property.ShouldSerialize = (_, _) => false;
    });
    return new(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
  }

  private sealed record StoredSnapshot(
    Guid TruckId,
    Guid RootDispatchId,
    DateTime CalculatedAt,
    string SummaryJson,
    string? CheckedRouteJson
  );

  private sealed record RouteEnvelope(
    TruckRoute? Checked,
    TruckRoute? Baseline
  );
}
