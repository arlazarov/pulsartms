using System.Text.Json;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.Routes;
using Npgsql;

internal static class FuelStationDiagnosis
{
  private static readonly Guid TruckId = Guid.Parse("341731b5-9440-43a3-adf1-ed7668c4d155");
  private static readonly Guid DispatchId = Guid.Parse("c0cf7891-4822-45ac-a1f1-347aa47b45ae");

  public static async Task RunAsync(NpgsqlConnection connection)
  {
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var ct = deadline.Token;
    await using var read = await connection.BeginTransactionAsync(ct);
    await using (var settings = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '10s'", connection, read))
      await settings.ExecuteNonQueryAsync(ct);
    TruckFuelPlanSnapshot snapshot;
    TruckRoute baseline;
    await using (var query = new NpgsqlCommand("""
      SELECT "SummaryJson", "CheckedRouteJson" FROM "TruckFuelPlans"
      WHERE "TruckId"=@truck AND "RootDispatchId"=@dispatch
        AND octet_length("SummaryJson")<=524288 AND octet_length("CheckedRouteJson")<=8388608
      LIMIT 1
      """, connection, read) { CommandTimeout = 10 })
    {
      query.Parameters.AddWithValue("truck", TruckId);
      query.Parameters.AddWithValue("dispatch", DispatchId);
      await using var row = await query.ExecuteReaderAsync(ct);
      if (!await row.ReadAsync(ct)) { Console.WriteLine("No bounded matching truck fuel snapshot found."); return; }
      snapshot = JsonSerializer.Deserialize<TruckFuelPlanSnapshot>(row.GetString(0), RoutePlanningService.Json)
        ?? throw new InvalidOperationException("The bounded snapshot is invalid.");
      baseline = JsonSerializer.Deserialize<Envelope>(row.GetString(1), RoutePlanningService.Json)?.Baseline
        ?? throw new InvalidOperationException("The snapshot has no saved baseline.");
    }
    if (snapshot.TruckId != TruckId || snapshot.RootDispatchId != DispatchId || snapshot.Stops.Count is < 1 or > 40
      || baseline.Legs.Count != snapshot.Stops.Count || baseline.Legs.Sum(leg => (long)leg.Points.Count) > 200_000)
      throw new InvalidOperationException("The snapshot exceeds the diagnostic scope or geometry bound.");
    var stations = new List<Station>();
    await using (var query = new NpgsqlCommand("""
      SELECT "Id", "Name", "Address", "City", "Region", "Latitude", "Longitude"
      FROM "FuelStations" WHERE upper("Name")=@name
        OR ("ExternalId"=@number AND "Address" ILIKE @address)
      LIMIT 3
      """, connection, read) { CommandTimeout = 10 })
    {
      query.Parameters.AddWithValue("name", "LOVES #333");
      query.Parameters.AddWithValue("number", "333");
      query.Parameters.AddWithValue("address", "%Sutton Ridge%");
      await using var rows = await query.ExecuteReaderAsync(ct);
      while (await rows.ReadAsync(ct))
        if (!rows.IsDBNull(5) && !rows.IsDBNull(6)) stations.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetString(2),
          rows.GetString(3), rows.GetString(4), new((double)rows.GetDecimal(5), (double)rows.GetDecimal(6))));
    }
    if (stations.Count != 1) { Console.WriteLine($"Expected one exact station; found {stations.Count} bounded matches."); return; }
    var station = stations[0];
    var quotes = new List<Quote>();
    var today = FuelPricingDate.FromUtc(DateTime.UtcNow);
    await using (var query = new NpgsqlCommand("""
      SELECT "Currency", "Product", "DiscountPrice", "EffectiveFrom", "EffectiveTo"
      FROM "FuelDiscounts" WHERE "FuelStationId"=@station AND "EffectiveFrom"<=@date AND "EffectiveTo">=@date
        AND "DiscountPrice">0
      ORDER BY "DiscountPrice" LIMIT 10
      """, connection, read) { CommandTimeout = 10 })
    {
      query.Parameters.AddWithValue("station", station.Id);
      query.Parameters.AddWithValue("date", today);
      await using var rows = await query.ExecuteReaderAsync(ct);
      while (await rows.ReadAsync(ct)) quotes.Add(new(rows.GetString(0), rows.GetString(1), rows.GetDecimal(2),
        rows.GetFieldValue<DateOnly>(3), rows.GetFieldValue<DateOnly>(4)));
    }
    await read.RollbackAsync(ct);
    var price = quotes.Where(q => q.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase)
      && q.Product.Contains("diesel", StringComparison.OrdinalIgnoreCase)
      && !q.Product.Contains("reefer", StringComparison.OrdinalIgnoreCase)).Select(q => (double?)q.Cash).Min() ?? 1;
    var priced = new PricedFuelStation(new() { StationId = station.Id, Name = station.Name, Point = station.Point }, price, price);
    var geometry = new FuelSearchGeometry(baseline, ct);
    var occurrences = FuelRouteOccurrences.Create(baseline, [priced], new FuelRegionOptions(), ct, geometry, FuelAccessEstimate.NearbyMiles);
    double offset = 0;
    var legs = baseline.Legs.Select((leg, index) =>
    {
      var match = geometry.MatchLeg(index, station.Point, ct);
      var along = offset + match.Along;
      offset += leg.Miles;
      var stop = snapshot.Stops[index];
      return new { leg = index, stop.DispatchId, stop.Stop.Id, stop.Stop.Job, stop.Stop.Point,
        role = stop.DispatchId == DispatchId ? "current" : "future", finalLeg = index == baseline.Legs.Count - 1,
        matchedBaselineMiles = along, geographicAwayMiles = match.Away,
        withinSearchRadius = match.Away <= FuelAccessEstimate.NearbyMiles,
        endpointFilterRejects = along < 1 || along >= baseline.Miles - 1,
        estimatedRoundTripMiles = FuelAccessEstimate.DistanceMiles(match.Away) * 2,
        occurrenceIncluded = occurrences.Any(candidate => candidate.LegIndex == index) };
    }).ToArray();
    var plan = snapshot.Plan;
    var profile = JsonSerializer.Deserialize<TruckRouteProfile>(plan.ProfileSignature, RoutePlanningService.Json);
    object? comparison = null;
    if (profile is not null && profile.UseIfta == plan.UsesIfta)
    {
      var quote = FuelRegionGrid.Prices([new(station.Id, "333", station.Name, station.Address, station.City,
        station.Region, "", "US", (decimal)station.Point.Latitude, (decimal)station.Point.Longitude,
        quotes.Select(q => new FuelDiscountDto(q.Currency, q.Product, q.Cash, q.Cash, 0, q.From, q.To, null,
          q.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase) ? "US gal" : "")).ToList())], profile, today).SingleOrDefault();
      var selected = plan.Stops.SingleOrDefault(stop => stop.Name == "LOVES #399");
      if (quote is not null && selected is not null && profile.Mpg is > 0)
      {
        var targetMiles = legs.Min(match => match.estimatedRoundTripMiles);
        var targetMinutes = FuelAccessEstimate.DrivingMinutes(targetMiles);
        var extraMiles = targetMiles - selected.DetourMiles;
        var extraMinutes = targetMinutes - selected.DetourMinutes;
        var extraGallons = extraMiles / profile.Mpg.Value;
        var fuelCost = extraGallons * quote.EconomicUsd;
        var timeCost = extraMinutes / 60 * profile.DriverHourlyCostUsd;
        var grossSavings = selected.BuyGallons * (selected.EconomicUsdPerGallon - quote.EconomicUsd);
        comparison = new { basis = "Same saved purchase quantity, comparing estimated access only; not a complete replacement itinerary",
          quantityGallons = selected.BuyGallons, selectedEconomicUsdPerGallon = selected.EconomicUsdPerGallon,
          targetEconomicUsdPerGallon = quote.EconomicUsd, grossSavingsUsd = grossSavings,
          targetAccessMiles = targetMiles, selectedAccessMiles = selected.DetourMiles, extraAccessMiles = extraMiles,
          targetAccessMinutes = targetMinutes, selectedAccessMinutes = selected.DetourMinutes, extraAccessMinutes = extraMinutes,
          extraFuelGallons = extraGallons, extraFuelCostUsd = fuelCost, extraDrivingCostUsd = timeCost,
          additionalEstimatedAccessCostUsd = fuelCost + timeCost, netEstimatedSavingsUsd = grossSavings - fuelCost - timeCost,
          commonStopCostUsd = profile.StopCostUsd, stopCostDifferenceUsd = 0,
          limitations = "Access is not road-verified. Additional purchases, terminal reserve/value and schedule waiting require complete itinerary comparison." };
      }
    }
    Console.WriteLine(JsonSerializer.Serialize(new { truckId = TruckId, dispatchId = DispatchId,
      snapshot.CalculatedAt, plan.SelectionVersion, plan.EstimatedStationAccess, plan.UsesIfta,
      searchRadiusMiles = FuelAccessEstimate.NearbyMiles,
      snapshotSearchRadiusMiles = plan.EstimatedStationAccess && plan.SelectionVersion == 21 ? 2 : (double?)null,
      plan.DispatchIds, baselineMiles = baseline.Miles, plan.RemainingMiles, plan.StartingGallons,
      plan.PurchaseCostUsd, plan.EconomicCostUsd, plan.ExpectedFutureFuelCostUsd,
      station, currentCashQuotes = quotes, estimatedPriceComparison = comparison,
      matches = legs, occurrences = occurrences.Select(candidate => new { candidate.LegIndex, candidate.AlongMiles,
        candidate.ExtraInMiles, candidate.VisitKey }),
      selectedStations = plan.Stops.Take(50).Select(stop => new { stop.Name, stop.StationId, stop.DispatchId,
        stop.BeforeStopId, stop.RouteMilesAhead, stop.MilesAhead, stop.BuyGallons, stop.FillToTarget,
        stop.ArrivalGallons, stop.DepartureGallons, stop.CashUsdPerGallon, stop.EconomicUsdPerGallon }),
      comparedChains = plan.RouteChecks.Take(12).Select(check => new { check.Stations, check.Result, check.CostUsd,
        check.ExtraMiles, check.ExtraMinutes })
    }, new JsonSerializerOptions { WriteIndented = true }));
  }

  private sealed record Envelope(TruckRoute? Checked, TruckRoute? Baseline);
  private sealed record Station(Guid Id, string Name, string Address, string City, string Region, RoutePoint Point);
  private sealed record Quote(string Currency, string Product, decimal Cash, DateOnly From, DateOnly To);
}
