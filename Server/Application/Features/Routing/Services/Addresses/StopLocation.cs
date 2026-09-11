using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Exceptions;
using Domain.Entities.Dispatch;
using Application.Features.Dispatch.Models;
using System.Text.Json;

namespace Application.Features.Routing.Services.Addresses;

public static class StopLocation
{
  public static readonly TimeSpan VerificationLifetime = TimeSpan.FromDays(29);
  public static string Address(DispatchStop stop) => string.Join(", ",
    string.Join(",", new[] { stop.Address, stop.City, stop.Province, stop.ZipCode, stop.Country })
      .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
      .Distinct(StringComparer.OrdinalIgnoreCase));

  public static Task<RoutePoint> ResolveAsync(DispatchStop stop, IRoutingProvider routing, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (VerifiedPoint(stop, DateTime.UtcNow) is { } verified) return Task.FromResult(verified);
    // Imported coordinates can identify a city rather than the stated delivery address.
    if (!string.IsNullOrWhiteSpace(stop.Address)) return routing.GeocodeAsync(Address(stop), ct);
    var point = stop.Latitude.HasValue && stop.Longitude.HasValue
      ? new RoutePoint((double)stop.Latitude, (double)stop.Longitude) : null;
    return point?.IsValid == true ? Task.FromResult(point)
      : throw new RoutePlanningException($"Stop {stop.Sequence} needs a street address or exact coordinates.");
  }

  public static RoutePoint? VerifiedPoint(DispatchStop stop, DateTime now)
  {
    if (string.IsNullOrWhiteSpace(stop.Address) || string.IsNullOrWhiteSpace(stop.SourceAddressJson)
      || stop.AddressVerifiedAt is not { } verified || verified > now || now - verified >= VerificationLifetime
      || stop.AddressRetryAfter.HasValue || stop.Latitude is null || stop.Longitude is null) return null;
    try
    {
      if (string.IsNullOrWhiteSpace(JsonSerializer.Deserialize<StopAddress>(stop.SourceAddressJson)?.Address)) return null;
    }
    catch (JsonException) { return null; }
    var point = new RoutePoint((double)stop.Latitude, (double)stop.Longitude);
    return point.IsValid ? point : null;
  }
}
