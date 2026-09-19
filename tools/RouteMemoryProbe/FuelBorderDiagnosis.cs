using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Infrastructure.Integrations.GeoTimeZone;
using Npgsql;

internal static class FuelBorderDiagnosis
{
  public static async Task RunAsync(
    NpgsqlConnection connection,
    string truckNumber
  )
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var ct = timeout.Token;
    await using var transaction = await connection.BeginTransactionAsync(ct);
    await using (
      var settings = new NpgsqlCommand(
        "SET TRANSACTION READ ONLY; SET LOCAL statement_timeout='10s'",
        connection,
        transaction
      )
    )
      await settings.ExecuteNonQueryAsync(ct);
    TruckFuelPlanSnapshot snapshot;
    TruckRoute baseline;
    int loadNumber;
    await using (
      var command = new NpgsqlCommand(
        """
        SELECT f."SummaryJson", f."CheckedRouteJson", d."LoadNumber"
        FROM "TruckFuelPlans" f
        JOIN "Trucks" t ON t."Id"=f."TruckId"
        JOIN "Dispatches" d ON d."Id"=f."RootDispatchId"
        WHERE t."UnitNumber"=@unit
          AND octet_length(f."SummaryJson")<=524288
          AND octet_length(f."CheckedRouteJson")<=8388608
        LIMIT 1
        """,
        connection,
        transaction
      )
    )
    {
      command.Parameters.AddWithValue("unit", truckNumber);
      await using var row = await command.ExecuteReaderAsync(ct);
      if (!await row.ReadAsync(ct))
        throw new InvalidOperationException("No bounded fuel snapshot found.");
      snapshot =
        JsonSerializer.Deserialize<TruckFuelPlanSnapshot>(
          row.GetString(0),
          RoutePlanningService.Json
        ) ?? throw new InvalidOperationException("Invalid fuel summary.");
      baseline =
        JsonSerializer
          .Deserialize<Envelope>(row.GetString(1), RoutePlanningService.Json)
          ?.Baseline
        ?? throw new InvalidOperationException("No saved baseline.");
      loadNumber = row.GetInt32(2);
    }
    if (
      snapshot.Stops.Count is < 1 or > 40
      || baseline.Legs.Count != snapshot.Stops.Count
      || baseline.Legs.Sum(leg => (long)leg.Points.Count) > 200_000
      || snapshot.Plan.Stops.Count > 50
    )
      throw new InvalidOperationException("Fuel diagnostic bound exceeded.");
    await transaction.RollbackAsync(ct);
    var regions = new RouteRegionLookup();
    var countries = new FuelAccessCountries(regions);
    var geometry = new FuelSearchGeometry(baseline, ct);
    var anchors = snapshot.Stops.ToList();
    var matches = snapshot.Plan.Stops.Select(stop =>
    {
      var index = anchors.FindIndex(anchor =>
        anchor.DispatchId == stop.DispatchId
        && anchor.Stop.Id == stop.BeforeStopId
      );
      if (index < 0)
        throw new InvalidOperationException("Fuel visit has no saved anchor.");
      var match = geometry.MatchLeg(index, stop.Point, ct);
      return new
      {
        stop.StationId,
        stop.Name,
        stop.DispatchId,
        stop.BeforeStopId,
        stop.Country,
        stop.Point,
        stationGeographicCountry = regions.Find(stop.Point).Country,
        matchedRoadPoint = match.Point,
        matchedRoadCountry = regions.Find(match.Point).Country,
        geographicAccessMiles = match.Away,
        estimatedRoundTripMiles = FuelAccessEstimate.DistanceMiles(match.Away)
          * 2,
        stop.DetourMiles,
        allowedByCountryGuard = countries.Matches(match.Point, stop),
      };
    });
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          readOnly = true,
          providerRequests = 0,
          truckNumber,
          loadNumber,
          snapshot.RootDispatchId,
          snapshot.CalculatedAt,
          snapshot.Plan.SelectionVersion,
          matches,
        }
      )
    );
  }

  private sealed record Envelope(TruckRoute? Baseline);
}
