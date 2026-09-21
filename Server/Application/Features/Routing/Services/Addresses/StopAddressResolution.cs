using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Rules;

namespace Application.Features.Routing.Services.Addresses;

internal static class StopAddressResolution
{
  public static async Task ApplyAsync(
    DispatchStop stop,
    IAddressGeocoder geocoder,
    DateTime now,
    CancellationToken ct
  )
  {
    stop.AddressVerifiedAt = null;
    if (stop.SourceAddressJson.Length == 0)
      stop.SourceAddressJson = StopAddress.From(stop).Serialize();
    if (ReadSource(stop.SourceAddressJson) is null)
    {
      stop.AddressRetryAfter = now.AddHours(24);
      return;
    }
    try
    {
      var resolved = await geocoder.ResolveAsync(
        StopLocation.Address(stop),
        ct
      );
      new StopAddress(
        resolved.Address,
        resolved.City,
        resolved.Province,
        resolved.Country,
        resolved.ZipCode
      ).Apply(stop);
      stop.Latitude = (decimal)resolved.Point.Latitude;
      stop.Longitude = (decimal)resolved.Point.Longitude;
      stop.AddressVerifiedAt = now;
      stop.AddressRetryAfter = null;
    }
    catch (RoutePlanningException ex)
    {
      stop.AddressRetryAfter =
        ex.RetryAfter < now.AddHours(24) ? ex.RetryAfter : now.AddHours(24);
    }
  }

  public static StopAddress? ReadSource(string json)
  {
    try
    {
      var source = JsonSerializer.Deserialize<StopAddress>(json);
      return string.IsNullOrWhiteSpace(source?.Address) ? null : source;
    }
    catch (JsonException)
    {
      return null;
    }
  }
}
