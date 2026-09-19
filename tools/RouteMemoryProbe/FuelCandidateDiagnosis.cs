using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Eta.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Interfaces;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal static class FuelCandidateDiagnosis
{
  public static async Task RunAsync(
    string truckNumber,
    IServiceProvider services,
    RoutePlanningState state,
    List<DispatchResponse> loads,
    CancellationToken ct
  )
  {
    var plan = state.Plan;
    if (plan is null || plan.InputsChanged)
      throw new InvalidOperationException("No valid saved current route.");
    var db = services.GetRequiredService<AppDbContext>();
    var routes = services.GetRequiredService<RoutePlanningService>();
    var externalId = await db
      .Trucks.Where(truck => truck.Id == plan.TruckId)
      .Select(truck => truck.ExternalId)
      .SingleAsync(ct);
    var checkpoint = await services
      .GetRequiredService<ISynchronizationStore>()
      .ReadAsync(ct);
    if (!checkpoint.Vehicles.TryGetValue(externalId, out var vehicle))
      throw new InvalidOperationException("No saved GPS observation.");
    var load = await routes.LoadAsync(
      plan.DispatchId,
      ct,
      plan.ExecutionLegId,
      plan.TruckId
    );
    state = state with
    {
      Progress = RoutePlanningService.Progress(
        plan,
        new TruckLocation
        {
          TruckId = plan.TruckId,
          Latitude = vehicle.Latitude,
          Longitude = vehicle.Longitude,
          UpdatedAt = vehicle.UpdatedAt,
        },
        load
      ),
      FuelPercent = (double?)vehicle.FuelPercent,
      FuelUpdatedAt = vehicle.FuelUpdatedAt,
    };
    if (state.Progress is not { LocationStale: false, Position.IsValid: true })
      throw new InvalidOperationException("No fresh saved GPS observation.");
    var regions = services.GetRequiredService<IRouteRegionLookup>();
    var options = services
      .GetRequiredService<IOptions<FuelRegionOptions>>()
      .Value;
    var date = FuelPricingDate.FromUtc(DateTime.UtcNow);
    var response = await services
      .GetRequiredService<ISender>()
      .Send(new GetFuelStationsQuery(date), ct);
    if (!response.Success || response.Response is not { } quoted)
      throw new InvalidOperationException("Saved station prices unavailable.");
    if (quoted.Count > 20_000)
      throw new InvalidOperationException("Station diagnostic bound exceeded.");
    var profile = state.Profile;
    var prices = FuelRegionGrid.Prices(quoted, profile, date);
    var horizon = await services
      .GetRequiredService<FuelHorizon>()
      .BuildAsync(state, profile, ct);
    var geometry = new FuelSearchGeometry(horizon.Route, ct);
    var countries = new FuelAccessCountries(regions);
    var occurrences = FuelRouteOccurrences.Create(
      horizon.Route,
      prices,
      options,
      ct,
      geometry,
      FuelAccessEstimate.NearbyMiles
    );
    var allowed = occurrences
      .Where(x => countries.Matches(geometry.At(x.AlongMiles, ct), x.Station))
      .ToList();
    var nearby = FuelAccessEstimate.Nearby(allowed);
    var points = horizon.Route.Legs.SelectMany(leg => leg.Points).ToArray();
    var south = (decimal)points.Min(point => point.Latitude) - 1;
    var north = (decimal)points.Max(point => point.Latitude) + 1;
    var west = (decimal)points.Min(point => point.Longitude) - 2;
    var east = (decimal)points.Max(point => point.Longitude) + 2;
    var local = await db
      .FuelStations.AsNoTracking()
      .Where(station =>
        station.Latitude >= south
        && station.Latitude <= north
        && station.Longitude >= west
        && station.Longitude <= east
      )
      .Select(station => new
      {
        station.Id,
        station.Name,
        station.Country,
        station.Latitude,
        station.Longitude,
        firstQuote = station.FuelDiscounts.Min(price =>
          (DateOnly?)price.EffectiveFrom
        ),
        lastQuote = station.FuelDiscounts.Max(price =>
          (DateOnly?)price.EffectiveTo
        ),
      })
      .Take(20_001)
      .ToListAsync(ct);
    if (local.Count > 20_000)
      throw new InvalidOperationException("Local station bound exceeded.");
    var converted = prices.ToDictionary(price => price.Station.StationId);
    var quotedById = quoted.ToDictionary(station => station.Id);
    var localMatches = local
      .Select(station =>
      {
        var point = new RoutePoint(
          (double)station.Latitude!.Value,
          (double)station.Longitude!.Value
        );
        var match = geometry.MatchLeg(0, point, ct);
        var offset = 0.0;
        var along = match.Along;
        for (var index = 1; index < horizon.Route.Legs.Count; index++)
        {
          offset += horizon.Route.Legs[index - 1].Miles;
          var other = geometry.MatchLeg(index, point, ct);
          if (other.Away >= match.Away)
            continue;
          match = other;
          along = offset + match.Along;
        }
        quotedById.TryGetValue(station.Id, out var quote);
        var eligible = FuelDisplayPrices
          .EligibleQuotes(quote?.Discounts ?? [], date)
          .ToArray();
        return new
        {
          station.Id,
          station.Name,
          station.Country,
          geographicCountry = regions.Find(point).Country,
          matchedCountry = regions.Find(match.Point).Country,
          sameCountry = countries.Matches(
            match.Point,
            new() { Point = point, Country = station.Country }
          ),
          roadMiles = Math.Round(along, 2),
          awayMiles = Math.Round(match.Away, 2),
          station.firstQuote,
          station.lastQuote,
          currentQuotes = quote?.Discounts.Count ?? 0,
          eligibleQuotes = eligible.Length,
          currencies = eligible.Select(price => price.Currency).Distinct(),
          iftaQuotes = eligible.Count(price => price.PriceAfterIfta > 0),
          converted = converted.ContainsKey(station.Id),
        };
      })
      .ToList();
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          readOnly = true,
          providerRequests = 0,
          truckNumber,
          plan.TruckId,
          plan.DispatchId,
          date,
          profile.UseIfta,
          profile.CadToUsd,
          gpsAt = vehicle.UpdatedAt,
          vehicle.FuelUpdatedAt,
          state.FuelPercent,
          exchangeRatePresent = profile.CadToUsd is > 0,
          horizon.Route.Miles,
          horizon.DispatchIds,
          originCountry = regions.Find(points[0]).Country,
          destinationCountry = regions.Find(points[^1]).Country,
          quotedStations = quoted.Count,
          quotesByCurrency = quoted
            .SelectMany(station => station.Discounts)
            .GroupBy(price => price.Currency)
            .Select(group => new
            {
              currency = group.Key,
              count = group.Count(),
            }),
          convertedByCountry = prices
            .GroupBy(price => regions.Find(price.Station.Point).Country)
            .Select(group => new
            {
              country = group.Key,
              count = group.Count(),
            }),
          occurrences = occurrences.Count,
          allowedByCountry = allowed
            .GroupBy(value => regions.Find(value.Station.Point).Country)
            .Select(group => new
            {
              country = group.Key,
              count = group.Count(),
            }),
          nearbyCount = nearby.Count,
          nearbyPrices = nearby
            .OrderBy(value => value.AlongMiles)
            .Take(100)
            .Select(value => new
            {
              value.Station.StationId,
              value.Station.Name,
              value.LegIndex,
              value.AlongMiles,
              value.PriceUsd,
              value.EconomicPriceUsd,
              value.ExtraInMiles,
              value.ExtraOutMiles,
            }),
          rejectedCountry = occurrences
            .Except(allowed)
            .Take(10)
            .Select(value => new
            {
              value.Station.StationId,
              value.Station.Name,
              stationCountry = regions.Find(value.Station.Point).Country,
              roadCountry = regions
                .Find(geometry.At(value.AlongMiles, ct))
                .Country,
              geographicAccessMiles = value.ExtraInMiles,
            }),
          localStationsByCountry = localMatches
            .GroupBy(value => value.geographicCountry)
            .Select(group => new
            {
              country = group.Key,
              total = group.Count(),
              currentQuoted = group.Count(value => value.currentQuotes > 0),
              eligible = group.Count(value => value.eligibleQuotes > 0),
              converted = group.Count(value => value.converted),
            }),
          nearestSameCountry = localMatches
            .Where(value => value.sameCountry)
            .OrderBy(value => value.awayMiles)
            .ThenBy(value => value.roadMiles)
            .Take(12),
        }
      )
    );
  }
}
