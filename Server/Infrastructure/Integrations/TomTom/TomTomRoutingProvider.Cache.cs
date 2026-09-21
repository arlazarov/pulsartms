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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.TomTom;

// Reading a route back out of the cache. A cached route is checked before
// it is deserialized: its point count is read off the raw JSON, so one that
// is too large is refused without ever being built.
public sealed partial class TomTomRoutingProvider
{
  private static TruckRoute ReadCachedRoute(string json, bool compact)
  {
    if (json.Length > MaximumCacheBytes)
      throw InvalidCachedRoute();
    var bytes = Encoding.UTF8.GetByteCount(json);
    if (bytes > MaximumCacheBytes)
      throw InvalidCachedRoute();
    var buffer = ArrayPool<byte>.Shared.Rent(bytes);
    TruckRoute route;
    try
    {
      Encoding.UTF8.GetBytes(json, buffer);
      var utf8 = buffer.AsSpan(0, bytes);
      ValidateCachedPointCounts(utf8);
      route =
        JsonSerializer.Deserialize<TruckRoute>(utf8, Json)
        ?? throw InvalidCachedRoute();
    }
    catch (JsonException)
    {
      throw InvalidCachedRoute();
    }
    finally
    {
      ArrayPool<byte>.Shared.Return(buffer);
    }
    if (
      route.Points is null
      || route.Legs is not { Count: > 0 }
      || route.Warnings is null
      || !double.IsFinite(route.Miles)
      || route.Miles <= 0
      || route.Points.Any(point => point?.IsValid != true)
      || route.Legs.Any(leg =>
        leg is null
        || !double.IsFinite(leg.Miles)
        || leg.Miles < 0
        || leg.Points is not { Count: >= 2 }
        || leg.Points.Any(point => point?.IsValid != true)
      )
    )
      throw InvalidCachedRoute();
    if (compact && route.Points.Count == 0)
      foreach (var leg in route.Legs)
        route.Points.AddRange(
          route.Points.Count == 0 ? leg.Points : leg.Points.Skip(1)
        );
    if (route.Points.Count < 2)
      throw InvalidCachedRoute();
    return CanonicalTiming(route);
  }

  private static void ValidateCachedPointCounts(ReadOnlySpan<byte> json)
  {
    var reader = new Utf8JsonReader(json);
    if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
      throw InvalidCachedRoute();
    var flatPoints = 0;
    var legPoints = 0;
    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
    {
      var legs = Property(ref reader, "legs");
      var points = Property(ref reader, "points");
      if (!reader.Read())
        throw InvalidCachedRoute();
      if (legs)
      {
        if (reader.TokenType != JsonTokenType.StartArray)
          throw InvalidCachedRoute();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
          if (reader.TokenType != JsonTokenType.StartObject)
            throw InvalidCachedRoute();
          var before = legPoints;
          while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
          {
            var coordinates = Property(ref reader, "points");
            if (!reader.Read())
              throw InvalidCachedRoute();
            if (coordinates)
              CountPoints(ref reader, ref legPoints);
            else
              reader.Skip();
          }
          if (legPoints - before < 2)
            throw InvalidCachedRoute();
        }
      }
      else if (points)
        CountPoints(ref reader, ref flatPoints);
      else
        reader.Skip();
    }
    if (reader.Read())
      throw InvalidCachedRoute();
  }

  private static bool Property(ref Utf8JsonReader reader, string name)
  {
    if (reader.TokenType != JsonTokenType.PropertyName)
      throw InvalidCachedRoute();
    return reader.ValueSpan.Length <= 128
      && string.Equals(
        reader.GetString(),
        name,
        StringComparison.OrdinalIgnoreCase
      );
  }

  private static void CountPoints(ref Utf8JsonReader reader, ref int total)
  {
    if (reader.TokenType != JsonTokenType.StartArray)
      throw InvalidCachedRoute();
    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
    {
      if (
        ++total > MaximumRoutePoints
        || reader.TokenType != JsonTokenType.StartObject
      )
        throw InvalidCachedRoute();
      reader.Skip();
    }
  }

  private static RoutePlanningException InvalidCachedRoute() =>
    new(
      "The saved TomTom route is incomplete or exceeds safe geometry limits. The saved plan has been kept.",
      DateTime.UtcNow.AddMinutes(5)
    );
}
