using System.Diagnostics;
using System.Text.Json;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Microsoft.Extensions.Configuration;
using Npgsql;

var deleteArgument = args.FirstOrDefault(x => x.StartsWith("--delete-completed=", StringComparison.Ordinal));
if (!args.Contains("--read-only") && deleteArgument is null)
  throw new InvalidOperationException("Pass --read-only to inspect the configured database. No API providers are started.");
var config = new ConfigurationBuilder().AddUserSecrets("amftms-api-local").Build();
var connectionString = config.GetConnectionString("DefaultConnection")
  ?? throw new InvalidOperationException("Local database configuration is missing.");
await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
if (args.Contains("--current-dispatch-54777"))
{
  if (!args.Contains("--read-only")) throw new InvalidOperationException("Assignment inspection requires --read-only.");
  await CurrentDispatchDiagnosis.RunAsync(connection);
  return;
}
if (args.Contains("--fuel-station-333"))
{
  if (!args.Contains("--read-only")) throw new InvalidOperationException("Station inspection requires --read-only.");
  await FuelStationDiagnosis.RunAsync(connection);
  return;
}
var etaArgument = args.FirstOrDefault(x => x.StartsWith("--eta-forecast=", StringComparison.Ordinal));
if (etaArgument is not null)
{
  if (!args.Contains("--read-only")) throw new InvalidOperationException("ETA inspection requires --read-only.");
  await using var read = await connection.BeginTransactionAsync();
  await using (var settings = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '10s'", connection, read))
    await settings.ExecuteNonQueryAsync();
  await using var query = new NpgsqlCommand("""
    SELECT "ForecastJson" FROM "DispatchEtaForecasts"
    WHERE "DispatchId" = @id AND octet_length("ForecastJson") <= 1048576
    """, connection, read);
  query.Parameters.AddWithValue("id", Guid.Parse(etaArgument.Split('=', 2)[1]));
  var json = (string?)await query.ExecuteScalarAsync();
  if (json is null) Console.WriteLine("No bounded saved ETA snapshot found.");
  else
  {
    using var document = JsonDocument.Parse(json);
    var value = document.RootElement;
    Console.WriteLine(JsonSerializer.Serialize(new {
      calculatedAt = value.GetProperty("calculatedAt"), validUntil = value.GetProperty("validUntil"),
      cycleAtCalculation = value.TryGetProperty("cycleAtCalculation", out var initial) ? initial : (JsonElement?)null,
      stops = value.GetProperty("stops").EnumerateArray().Select(stop => new {
        stopId = stop.GetProperty("stopId"), arrival = stop.GetProperty("arrival"), departure = stop.GetProperty("departure"),
        drivingMinutes = stop.GetProperty("drivingMinutes"), restMinutes = stop.GetProperty("restMinutes"),
        cycleAfterDeparture = stop.TryGetProperty("cycleAfterDeparture", out var cycle) ? cycle : (JsonElement?)null
      }).ToArray()
    }, new JsonSerializerOptions { WriteIndented = true }));
  }
  await read.RollbackAsync();
  return;
}
var fuelArgument = args.FirstOrDefault(x => x.StartsWith("--fuel-plan=", StringComparison.Ordinal));
var matchArgument = args.FirstOrDefault(x => x.StartsWith("--fuel-match=", StringComparison.Ordinal));
if (matchArgument is not null)
{
  if (!args.Contains("--read-only")) throw new InvalidOperationException("Fuel matching inspection requires --read-only.");
  await using var read = await connection.BeginTransactionAsync();
  await using (var settings = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '10s'", connection, read))
    await settings.ExecuteNonQueryAsync();
  await using var query = new NpgsqlCommand("""
    SELECT p."PlanJson", c."StateJson"::jsonb->'vehicles'->t."ExternalId"
    FROM "DispatchRoutePlans" p JOIN "Trucks" t ON t."Id"=p."TruckId"
    JOIN "SynchronizationCheckpoints" c ON c."Id"='6acb468e-c923-481f-b9c9-6363febf4c0a'
    WHERE p."DispatchId"=@id AND octet_length(p."PlanJson")<=16777216
    """, connection, read);
  query.Parameters.AddWithValue("id", Guid.Parse(matchArgument.Split('=', 2)[1]));
  await using var row = await query.ExecuteReaderAsync();
  if (!await row.ReadAsync() || row.IsDBNull(1)) Console.WriteLine("No bounded route/telemetry snapshot found.");
  else
  {
    var plan = JsonSerializer.Deserialize<RoutePlan>(row.GetString(0), RoutePlanningService.Json)!;
    var vehicle = JsonSerializer.Deserialize<Application.Features.Fleet.Models.VehicleTelemetry>(row.GetString(1), RoutePlanningService.Json)!;
    var point = new RoutePoint((double)vehicle.Latitude, (double)vehicle.Longitude);
    var index = plan.Stops.FindIndex(stop => stop.Id == plan.Tracking.NextStopId);
    var legIndex = plan.FromCurrentPosition ? index : index - 1;
    if (legIndex < 0 || legIndex >= plan.Route.Legs.Count) Console.WriteLine("Current stop has no saved road leg.");
    else
    {
      var match = new Application.Features.Routing.Algorithms.RouteGeometry(new() { Legs = [plan.Route.Legs[legIndex]] }).Match(point);
      Console.WriteLine(JsonSerializer.Serialize(new { plan.Version, plan.FromCurrentPosition,
        stops = plan.Stops.Count, legs = plan.Route.Legs.Count, vehicle.UpdatedAt,
        matchedRoadMiles = match.Along, distanceFromSavedRoadMiles = match.Away,
        fuelTrimMatch = match.Away <= .05, normalProjectionMatch = match.Away <= .15 }));
    }
  }
  await row.CloseAsync();
  await read.RollbackAsync();
  return;
}
if (fuelArgument is not null)
{
  await using var query = new NpgsqlCommand("SELECT \"PlanJson\" FROM \"DispatchRoutePlans\" WHERE \"DispatchId\" = @id", connection);
  query.Parameters.AddWithValue("id", Guid.Parse(fuelArgument.Split('=', 2)[1]));
  var json = (string?)await query.ExecuteScalarAsync();
  var plan = json is null ? null : JsonSerializer.Deserialize<RoutePlan>(json, RoutePlanningService.Json);
  Console.WriteLine(JsonSerializer.Serialize(new { plan?.DispatchId, plan?.Profile,
    fuel = plan?.FuelPlan is {} f ? new { f.CalculatedAt, f.RemainingMiles, f.StartingGallons, f.ArrivalGallons,
      f.UsesIfta, f.PurchaseCostUsd, f.EconomicCostUsd, f.ProfileSignature, f.DispatchIds,
      stops = f.Stops.Select(s => new { s.Name, s.MilesAhead, s.CurrentRouteMile, s.BuyGallons, s.ArrivalGallons, s.DepartureGallons, s.EconomicPrice }), f.RouteChecks } : null }, new JsonSerializerOptions { WriteIndented = true }));
  return;
}
if (deleteArgument is not null)
{
  var id = Guid.Parse(deleteArgument.Split('=', 2)[1]);
  await using var deletion = new NpgsqlCommand("""
    DELETE FROM "DispatchRoutePlans" p USING "Dispatches" d
    WHERE p."DispatchId" = @id AND d."Id" = p."DispatchId"
      AND EXISTS (SELECT 1 FROM
        (SELECT "Job", "DeliveredAt", "DepartedAt" FROM "DispatchStops"
         WHERE "DispatchId" = d."Id" ORDER BY "Sequence" DESC LIMIT 1) final
        WHERE final."Job" = 'Drop Off' AND (final."DeliveredAt" IS NOT NULL OR final."DepartedAt" IS NOT NULL))
    RETURNING d."LoadNumber", octet_length(p."PlanJson")::bigint
    """, connection);
  deletion.Parameters.AddWithValue("id", id);
  await using var reader = await deletion.ExecuteReaderAsync();
  var deleted = 0;
  while (await reader.ReadAsync()) { deleted++; Console.WriteLine(JsonSerializer.Serialize(new { deletedLoad = reader.GetInt32(0), removedJsonBytes = reader.GetInt64(1) })); }
  if (deleted != 1) throw new InvalidOperationException("No completed route was deleted; recheck its current state.");
  return;
}
if (args.Contains("--completion-status"))
{
  await using var query = new NpgsqlCommand("""
    SELECT p."DispatchId", d."LoadNumber", d."Status", octet_length(p."PlanJson")::bigint,
           s."Job", s."DeliveredAt", s."DepartedAt"
    FROM "DispatchRoutePlans" p JOIN "Dispatches" d ON d."Id" = p."DispatchId"
    LEFT JOIN LATERAL (SELECT "Job", "DeliveredAt", "DepartedAt" FROM "DispatchStops"
      WHERE "DispatchId" = d."Id" ORDER BY "Sequence" DESC LIMIT 1) s ON true
    ORDER BY d."LoadNumber"
    """, connection);
  await using var reader = await query.ExecuteReaderAsync();
  while (await reader.ReadAsync()) Console.WriteLine(JsonSerializer.Serialize(new {
    id = reader.GetGuid(0), load = reader.GetInt32(1), status = reader.GetString(2), bytes = reader.GetInt64(3),
    lastJob = reader.IsDBNull(4) ? null : reader.GetString(4), delivered = reader.IsDBNull(5) ? (DateTime?)null : reader.GetDateTime(5),
    departed = reader.IsDBNull(6) ? (DateTime?)null : reader.GetDateTime(6) }));
  return;
}
await using var transaction = await connection.BeginTransactionAsync();
await using (var settings = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '20s'", connection, transaction))
  await settings.ExecuteNonQueryAsync();
var rows = new List<(Guid Id, string Truck, int Load, long Bytes)>();
await using (var command = new NpgsqlCommand("""
  SELECT p."DispatchId", t."UnitNumber", d."LoadNumber", octet_length(p."PlanJson")::bigint
  FROM "DispatchRoutePlans" p
  JOIN "Trucks" t ON t."Id" = p."TruckId"
  JOIN "Dispatches" d ON d."Id" = p."DispatchId"
  ORDER BY t."UnitNumber", d."LoadNumber"
  """, connection, transaction))
await using (var reader = await command.ExecuteReaderAsync())
  while (await reader.ReadAsync()) rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt64(3)));
foreach (var group in rows.GroupBy(x => x.Truck))
  Console.WriteLine(JsonSerializer.Serialize(new { type = "truck_storage", truck = group.Key, plans = group.Count(), bytes = group.Sum(x => x.Bytes), largestBytes = group.Max(x => x.Bytes) }));
foreach (var row in rows)
{
  if (row.Bytes > 16 * 1024 * 1024)
  {
    Console.WriteLine(JsonSerializer.Serialize(new { type = "oversized", truck = row.Truck, load = row.Load, bytes = row.Bytes, reason = "Skipped parsing above 16 MiB safety limit" }));
    continue;
  }
  await using var command = new NpgsqlCommand("SELECT \"PlanJson\" FROM \"DispatchRoutePlans\" WHERE \"DispatchId\" = @id", connection, transaction);
  command.Parameters.AddWithValue("id", row.Id);
  var json = (string)(await command.ExecuteScalarAsync())!;
  if (args.Contains("--geometry-detail"))
  {
    var plan = JsonSerializer.Deserialize<RoutePlan>(json, RoutePlanningService.Json)!;
    var legs = plan.Route.Legs.Concat(plan.ReferenceRoute?.Legs ?? []).ToList();
    var counts = new[] { 10d, 2d }.Select(tolerance => new {
      toleranceMeters = tolerance,
      points = legs.Sum(leg => Application.Features.Routing.Algorithms.DisplayRouteGeometry.Simplify(leg.Points, tolerance).Count)
    });
    Console.WriteLine(JsonSerializer.Serialize(new { type = "geometry_detail", truck = row.Truck, load = row.Load,
      sourcePoints = legs.Sum(leg => leg.Points.Count), counts }));
    continue;
  }
  Measure(json);
  var measurement = Measure(json);
  Console.WriteLine(JsonSerializer.Serialize(new { type = "route_memory", truck = row.Truck, load = row.Load, storedBytes = row.Bytes,
    stringBytes = 2L * json.Length, measurement.Retained, measurement.Allocated, measurement.Milliseconds, measurement.Points, measurement.Legs, measurement.Stops }));
  MeasureDisplay(json);
  var display = MeasureDisplay(json);
  Console.WriteLine(JsonSerializer.Serialize(new { type = "display_memory", truck = row.Truck, load = row.Load, display }));
}
await transaction.RollbackAsync();

static Measurement Measure(string json)
{
  var baseline = GC.GetTotalMemory(true);
  var allocated = GC.GetAllocatedBytesForCurrentThread();
  var watch = Stopwatch.StartNew();
  var plan = JsonSerializer.Deserialize<RoutePlan>(json, RoutePlanningService.Json)!;
  watch.Stop();
  allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
  var retained = Math.Max(0, GC.GetTotalMemory(true) - baseline);
  var points = plan.Route.Points.Count + plan.Route.Legs.Sum(x => x.Points.Count)
    + (plan.ReferenceRoute?.Points.Count ?? 0) + (plan.ReferenceRoute?.Legs.Sum(x => x.Points.Count) ?? 0);
  var result = new Measurement(retained, allocated, watch.Elapsed.TotalMilliseconds, points, plan.Route.Legs.Count, plan.Stops.Count);
  GC.KeepAlive(plan);
  return result;
}

static object MeasureDisplay(string json)
{
  var baseline = GC.GetTotalMemory(true);
  var snapshot = RouteDisplayCache.Create(new() { PlanJson = json });
  var retained = Math.Max(0, GC.GetTotalMemory(true) - baseline);
  var before = GC.GetAllocatedBytesForCurrentThread();
  var watch = Stopwatch.StartNew();
  var plan = snapshot.ReadPlan();
  watch.Stop();
  var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
  var result = new { RetainedSnapshotBytes = retained, ConservativeCacheBytes = snapshot.Size, DisplayJsonBytes = snapshot.DisplayBytes,
    WarmReadAllocatedBytes = allocated, WarmReadMilliseconds = watch.Elapsed.TotalMilliseconds,
    DisplayPoints = plan.Route.Legs.Sum(x => x.Points.Count) + (plan.ReferenceRoute?.Legs.Sum(x => x.Points.Count) ?? 0) };
  GC.KeepAlive(snapshot);
  GC.KeepAlive(plan);
  return result;
}

record Measurement(long Retained, long Allocated, double Milliseconds, int Points, int Legs, int Stops);
