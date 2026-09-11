using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Domain.Entities.Fuel;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class TruckFuelPlanStore(AppDbContext db) : ITruckFuelPlanStore
{
  private const int MaximumSummaryBytes = 512 * 1024;
  private const int MaximumRouteBytes = 8 * 1024 * 1024;
  private const int MaximumRoutePoints = 200_000;
  private const int MaximumItineraryStops = 40;
  private static readonly JsonSerializerOptions Json = CreateJson();

  public async Task<TruckFuelPlanSnapshot?> ReadAsync(Guid truckId, bool includeRoute, CancellationToken ct)
  {
    var query = db.Set<TruckFuelPlan>().AsNoTracking().Where(x => x.TruckId == truckId);
    var row = includeRoute
      ? await query.Select(x => new StoredSnapshot(x.TruckId, x.RootDispatchId, x.CalculatedAt, x.SummaryJson, x.CheckedRouteJson)).SingleOrDefaultAsync(ct)
      : await query.Select(x => new StoredSnapshot(x.TruckId, x.RootDispatchId, x.CalculatedAt, x.SummaryJson, null)).SingleOrDefaultAsync(ct);
    if (row is null || !WithinBytes(row.SummaryJson, MaximumSummaryBytes)
      || row.CheckedRouteJson is { } rawRoute && !WithinBytes(rawRoute, MaximumRouteBytes)) return null;
    try
    {
      var snapshot = JsonSerializer.Deserialize<TruckFuelPlanSnapshot>(row.SummaryJson, Json);
      if (snapshot is null || snapshot.CheckedRoute is not null || snapshot.BaselineRoute is not null || !ValidSummary(snapshot)
        || snapshot.TruckId != row.TruckId || snapshot.RootDispatchId != row.RootDispatchId
        || DatabaseInstant(snapshot.CalculatedAt) != row.CalculatedAt) return null;
      var geometry = row.CheckedRouteJson is null ? null : JsonSerializer.Deserialize<RouteEnvelope>(row.CheckedRouteJson, Json);
      if (row.CheckedRouteJson is not null && (geometry is null || geometry.Checked is null && geometry.Baseline is null)) return null;
      var result = snapshot with { CheckedRoute = geometry?.Checked, BaselineRoute = geometry?.Baseline };
      if (includeRoute && result.Plan.EstimatedStationAccess && result.BaselineRoute is null) return null;
      return ValidRoutes(result) ? result : null;
    }
    catch (JsonException) { return null; }
  }

  public async Task<bool> SaveAsync(TruckFuelPlanSnapshot snapshot, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    var (summary, checkedRoute) = Serialize(snapshot);
    // One conditional statement protects the complete result from stale concurrent writers.
    return await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO "TruckFuelPlans" ("Id", "TruckId", "RootDispatchId", "CalculatedAt", "SummaryJson", "CheckedRouteJson")
      VALUES ({snapshot.TruckId}, {snapshot.TruckId}, {snapshot.RootDispatchId}, {DatabaseInstant(snapshot.CalculatedAt)}, {summary}, {checkedRoute})
      ON CONFLICT ("TruckId") DO UPDATE SET
        "RootDispatchId" = excluded."RootDispatchId", "CalculatedAt" = excluded."CalculatedAt",
        "SummaryJson" = excluded."SummaryJson", "CheckedRouteJson" = excluded."CheckedRouteJson"
      WHERE "TruckFuelPlans"."CalculatedAt" < excluded."CalculatedAt"
      """, ct) > 0;
  }

  public async Task<bool> ReplaceAsync(TruckFuelPlanSnapshot snapshot, DateTime? expectedCalculatedAt, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (expectedCalculatedAt is { } expected && (expected.Kind != DateTimeKind.Utc || expected == default))
      throw new ArgumentException("The expected fuel plan revision must be a non-default UTC instant.", nameof(expectedCalculatedAt));
    var (summary, checkedRoute) = Serialize(snapshot);
    var calculatedAt = DatabaseInstant(snapshot.CalculatedAt);
    if (expectedCalculatedAt is not { } revision)
      return await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "TruckFuelPlans" ("Id", "TruckId", "RootDispatchId", "CalculatedAt", "SummaryJson", "CheckedRouteJson")
        VALUES ({snapshot.TruckId}, {snapshot.TruckId}, {snapshot.RootDispatchId}, {calculatedAt}, {summary}, {checkedRoute})
        ON CONFLICT ("TruckId") DO NOTHING
        """, ct) > 0;

    // A later calculation timestamp cannot authorize overwriting a plan changed since the edit began.
    return await db.Database.ExecuteSqlInterpolatedAsync($"""
      UPDATE "TruckFuelPlans"
      SET "RootDispatchId" = {snapshot.RootDispatchId}, "CalculatedAt" = {calculatedAt},
        "SummaryJson" = {summary}, "CheckedRouteJson" = {checkedRoute}
      WHERE "TruckId" = {snapshot.TruckId} AND "CalculatedAt" = {DatabaseInstant(revision)}
        AND "CalculatedAt" < {calculatedAt}
      """, ct) > 0;
  }

  private static (string Summary, string? CheckedRoute) Serialize(TruckFuelPlanSnapshot snapshot)
  {
    if (!ValidSummary(snapshot) || !ValidRoutes(snapshot)
      || snapshot.Plan.EstimatedStationAccess && snapshot.BaselineRoute is null)
      throw new ArgumentException("The truck fuel snapshot is incomplete or exceeds the supported itinerary bounds.", nameof(snapshot));
    var summary = JsonSerializer.Serialize(snapshot with { CheckedRoute = null, BaselineRoute = null }, Json);
    var checkedRoute = snapshot.CheckedRoute is null && snapshot.BaselineRoute is null ? null
      : JsonSerializer.Serialize(new RouteEnvelope(snapshot.CheckedRoute, snapshot.BaselineRoute), Json);
    if (!WithinBytes(summary, MaximumSummaryBytes) || checkedRoute is not null && !WithinBytes(checkedRoute, MaximumRouteBytes))
      throw new ArgumentException("The truck fuel snapshot exceeds the supported payload size.", nameof(snapshot));
    return (summary, checkedRoute);
  }

  private static bool ValidSummary(TruckFuelPlanSnapshot? value)
  {
    if (value is null || value.TruckId == Guid.Empty || value.RootDispatchId == Guid.Empty
      || value.CalculatedAt.Kind != DateTimeKind.Utc || value.CalculatedAt == default
      || value.Plan is not { } plan || plan.TruckId != value.TruckId || plan.CalculatedAt != value.CalculatedAt
      || plan.DispatchIds is not { Count: >= 1 and <= MaximumItineraryStops } dispatches || dispatches[0] != value.RootDispatchId
      || dispatches.Any(x => x == Guid.Empty) || dispatches.Distinct().Count() != dispatches.Count
      || value.Stops is not { Count: >= 1 and <= MaximumItineraryStops } stops
      || plan.Stops is not { Count: <= 50 } purchases || plan.Notes is null || plan.RefreshReasons is null
      || plan.RouteChecks is null || plan.DispatchSignatures is null
      || !double.IsFinite(plan.RemainingMiles) || plan.RemainingMiles < 0
      || !double.IsFinite(plan.StartAccessMiles) || plan.StartAccessMiles < 0) return false;
    double previous = 0;
    var seen = new HashSet<Guid>();
    var owners = new List<Guid>();
    var intervals = new Dictionary<Guid, (Guid DispatchId, double Start, double End)>();
    foreach (var item in stops)
    {
      if (item?.Stop is not { } stop || !dispatches.Contains(item.DispatchId) || stop.Id == Guid.Empty
        || !seen.Add(stop.Id) || stop.Point?.IsValid != true || !double.IsFinite(item.EndMiles) || item.EndMiles < previous) return false;
      if (owners.Count == 0 || owners[^1] != item.DispatchId) owners.Add(item.DispatchId);
      intervals.Add(stop.Id, (item.DispatchId, previous, item.EndMiles));
      previous = item.EndMiles;
    }
    if (!owners.SequenceEqual(dispatches)) return false;
    var visits = new HashSet<string>(StringComparer.Ordinal);
    double purchaseMiles = -1;
    foreach (var purchase in purchases)
    {
      var routeMiles = plan.EstimatedStationAccess ? purchase?.RouteMilesAhead : purchase?.MilesAhead;
      if (purchase is null || purchase.StationId == Guid.Empty || purchase.Point?.IsValid != true
        || string.IsNullOrWhiteSpace(purchase.VisitKey) || !visits.Add(purchase.VisitKey)
        || !intervals.TryGetValue(purchase.BeforeStopId, out var interval) || interval.DispatchId != purchase.DispatchId
        || !double.IsFinite(purchase.MilesAhead) || purchase.MilesAhead < 0
        || routeMiles is not { } mile || !double.IsFinite(mile) || mile < 0 || mile < purchaseMiles
        || mile < interval.Start - .01 || mile > interval.End + .01
        || plan.EstimatedStationAccess && (!double.IsFinite(purchase.DetourMiles) || purchase.DetourMiles < 0
          || !double.IsFinite(purchase.DetourMinutes) || purchase.DetourMinutes < 0)
        || !double.IsFinite(purchase.BuyGallons) || purchase.BuyGallons < 0) return false;
      purchaseMiles = mile;
    }
    return true;
  }

  private static bool ValidRoutes(TruckFuelPlanSnapshot value)
  {
    if (value.Plan.EstimatedStationAccess)
      return value.CheckedRoute is null && (value.BaselineRoute is null || ValidRoute(value.BaselineRoute, value.Stops, true));
    if (value.CheckedRoute is { } route && !ValidRoute(route, value.Stops, true)
      || value.BaselineRoute is { } baseline && !ValidRoute(baseline, value.Stops, false)) return false;
    if (value.CheckedRoute is not { } checkedRoute || value.BaselineRoute is not { } baselineRoute) return true;
    return SameEndpoint(checkedRoute.Legs[0].Points[0], baselineRoute.Legs[0].Points[0])
      && checkedRoute.Legs.Zip(baselineRoute.Legs).All(pair => SameEndpoint(pair.First.Points[^1], pair.Second.Points[^1]));
  }

  private static bool ValidRoute(TruckRoute route, IReadOnlyList<FuelItineraryStop> stops, bool compareEndMiles)
  {
    if (!double.IsFinite(route.Miles) || route.Miles <= 0 || !double.IsFinite(route.Seconds) || route.Seconds < 0
      || route.Legs is not { Count: >= 1 and <= MaximumItineraryStops } legs || legs.Count != stops.Count
      || route.Warnings is null || route.Points is null || route.Points.Count > MaximumRoutePoints) return false;
    long points = 0;
    double miles = 0;
    double seconds = 0;
    for (var i = 0; i < legs.Count; i++)
    {
      var leg = legs[i];
      if (leg is null || leg.Points is not { Count: >= 2 } coordinates
        || !double.IsFinite(leg.Miles) || leg.Miles < 0 || !double.IsFinite(leg.Seconds) || leg.Seconds < 0) return false;
      points += coordinates.Count;
      if (points > MaximumRoutePoints || coordinates.Any(x => x?.IsValid != true)) return false;
      if (!SameEndpoint(coordinates[^1], stops[i].Stop.Point, .5)
        || i > 0 && !SameEndpoint(legs[i - 1].Points[^1], coordinates[0])) return false;
      miles += leg.Miles;
      seconds += leg.Seconds;
      if (compareEndMiles && Math.Abs(stops[i].EndMiles - miles) > .01) return false;
    }
    return Math.Abs(route.Miles - miles) <= .01 && Math.Abs(route.Seconds - seconds) <= .01;
  }

  private static bool SameEndpoint(RoutePoint first, RoutePoint second, double toleranceMiles = .05)
  {
    // Provider road snapping can slightly change the coordinate of the same mandatory stop.
    var latitudeMiles = (first.Latitude - second.Latitude) * 69;
    var longitudeMiles = (first.Longitude - second.Longitude) * 69
      * Math.Cos((first.Latitude + second.Latitude) * Math.PI / 360);
    return latitudeMiles * latitudeMiles + longitudeMiles * longitudeMiles <= toleranceMiles * toleranceMiles;
  }

  private static bool WithinBytes(string value, int maximum) => value.Length <= maximum && Encoding.UTF8.GetByteCount(value) <= maximum;
  private static DateTime DatabaseInstant(DateTime value) => new(value.Ticks / 10 * 10, DateTimeKind.Utc);

  private static JsonSerializerOptions CreateJson()
  {
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(info =>
    {
      if (info.Type != typeof(TruckRoute)) return;
      foreach (var property in info.Properties.Where(x => x.Name == "points")) property.ShouldSerialize = (_, _) => false;
    });
    return new(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
  }

  private sealed record StoredSnapshot(Guid TruckId, Guid RootDispatchId, DateTime CalculatedAt, string SummaryJson, string? CheckedRouteJson);
  private sealed record RouteEnvelope(TruckRoute? Checked, TruckRoute? Baseline);
}
