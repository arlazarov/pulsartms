using System.Buffers;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Diagnostics;
using Application.Features.Routing.Interfaces;
using Application.Interfaces;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Ports;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.TomTom;

public sealed partial class TomTomRoutingProvider(
  HttpClient http,
  IConfiguration configuration,
  IAppDbContext db,
  IRouteRequestValidator validator,
  IAddressGeocoder geocoder,
  IRouteSectionValidator sectionsValidator,
  IRouteRegionLookup regions
) : IRoutingProvider, IRouteAlternativesProvider
{
  private static readonly TomTomRequestGates RequestGates = new();
  private static readonly SemaphoreSlim ReservationGate = new(1, 1);

  // Keep the byte/point exposure bounded while unrelated requests wait on their
  // own HTTP responses.
  private static readonly SemaphoreSlim ProviderSlots = new(2, 2);
  private static readonly JsonSerializerOptions Json = new(
    JsonSerializerDefaults.Web
  );
  private const int MaximumResponseBytes = 16 * 1024 * 1024;
  private const int MaximumCacheBytes = 32 * 1024 * 1024;
  private const int MaximumRoutePoints = 200_000;
  public bool IsConfigured =>
    !string.IsNullOrWhiteSpace(configuration["TomTom:ApiKey"]);

  private static string Number(double v) =>
    v.ToString("0.######", CultureInfo.InvariantCulture);

  public async Task<TruckRoute> CalculateAsync(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile p,
    CancellationToken ct
  )
  {
    validator.Validate(points, p);
    var oneCountry = OneCountry(points);
    var query = Query(points, p, 0, oneCountry);
    var road = await CachedAsync(
      "route",
      query,
      TimeSpan.FromHours(12),
      root => ParseRoute(root, points.Count - 1),
      ReadCachedRoute,
      route =>
        JsonSerializer.Serialize(
          new
          {
            route.CalculatedAt,
            route.Miles,
            route.Seconds,
            route.Legs,
            route.Warnings,
          },
          Json
        ),
      ct
    );
    if (
      oneCountry
      && RouteBorderPolicy.Check(road, points, regions, ct)
        is { Leaves: true } exit
    )
      throw LeavesCountry(exit);
    return road;
  }

  // The border policy is a preference to the provider, so what it returns
  // is checked. A road that leaves the country anyway is neither used nor
  // saved, and planning says why. A road the check cannot fully place is
  // used; its saved verdict says unknown, which the auditor reports.
  private static RoutePlanningException LeavesCountry(BorderVerdict exit) =>
    new(
      $"TomTom's road leaves {exit.Country} through {exit.Entered} although "
        + $"every stop is in {exit.Country}. It was not used; review the "
        + "route or add a waypoint."
    );

  public async Task<IReadOnlyList<TruckRoute>> CalculateAlternativesAsync(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    validator.Validate(points, profile);
    var oneCountry = OneCountry(points);
    var roads = await CachedAsync(
      "alternatives",
      Query(points, profile, 2, oneCountry),
      TimeSpan.FromMinutes(15),
      root =>
      {
        if (
          root.ValueKind != JsonValueKind.Object
          || !root.TryGetProperty("routes", out var routes)
          || routes.ValueKind != JsonValueKind.Array
          || routes.GetArrayLength() is < 1 or > 3
        )
          throw InvalidCachedRoute();
        var result = Enumerable
          .Range(0, routes.GetArrayLength())
          .Select(index => ParseRoute(root, points.Count - 1, index))
          .ToList();
        if (
          result.Sum(route => route.Legs.Sum(leg => leg.Points.Count))
          > MaximumRoutePoints
        )
          throw ResponseTooLarge();
        return result;
      },
      (json, _) =>
      {
        if (
          json.Length > MaximumCacheBytes
          || Encoding.UTF8.GetByteCount(json) > MaximumCacheBytes
        )
          throw InvalidCachedRoute();
        try
        {
          using var document = JsonDocument.Parse(json);
          if (
            document.RootElement.ValueKind != JsonValueKind.Array
            || document.RootElement.GetArrayLength() is < 1 or > 3
          )
            throw InvalidCachedRoute();
          var result = document
            .RootElement.EnumerateArray()
            .Select(route => ReadCachedRoute(route.GetRawText(), true))
            .ToList();
          if (
            result.Sum(route => route.Legs.Sum(leg => leg.Points.Count))
            > MaximumRoutePoints
          )
            throw InvalidCachedRoute();
          return result;
        }
        catch (JsonException)
        {
          throw InvalidCachedRoute();
        }
      },
      routes =>
        JsonSerializer.Serialize(
          routes.Select(route => new
          {
            route.CalculatedAt,
            route.Miles,
            route.Seconds,
            route.Legs,
            route.Warnings,
          }),
          Json
        ),
      ct
    );
    if (!oneCountry || roads.Count == 0)
      return roads;
    var kept = roads
      .Where(road => !RouteBorderPolicy.Check(road, points, regions, ct).Leaves)
      .ToList();
    return kept.Count > 0
      ? kept
      : throw LeavesCountry(
        RouteBorderPolicy.Check(roads[0], points, regions, ct)
      );
  }

  private bool OneCountry(IReadOnlyList<RoutePoint> points) =>
    RouteBorderPolicy.KeepsToOneCountry(points.Select(regions.Find));

  // The request is the cache key, so a road asked for with the border
  // policy is never answered by one bought without it.
  private static string Query(
    IReadOnlyList<RoutePoint> points,
    TruckRouteProfile p,
    int alternatives,
    bool oneCountry
  )
  {
    var path = string.Join(
      ":",
      points.Select(x => $"{Number(x.Latitude)},{Number(x.Longitude)}")
    );
    var query =
      $"routing/1/calculateRoute/{path}/json?travelMode=truck&vehicleCommercial=true&routeType=fastest"
      + $"&traffic=true&maxAlternatives={alternatives}&sectionType=travelMode&report=effectiveSettings"
      + $"&vehicleHeight={Number(p.HeightFeet * .3048)}&vehicleWidth={Number(p.WidthFeet * .3048)}"
      + $"&vehicleLength={Number(p.LengthFeet * .3048)}&vehicleWeight={Math.Ceiling(p.WeightPounds * .45359237)}"
      + $"&vehicleAxleWeight={Math.Ceiling(p.AxleWeightPounds * .45359237)}&vehicleNumberOfAxles={p.Axles}";
    if (p.Hazmat != "")
      query += $"&vehicleLoadType={Uri.EscapeDataString(p.Hazmat)}";
    if (oneCountry)
      query += "&avoid=borderCrossings";
    return query;
  }

  public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct) =>
    geocoder.GeocodeAsync(address, ct);
}
