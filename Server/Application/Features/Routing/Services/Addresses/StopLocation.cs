using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Addresses;

public static class StopLocation
{
  public static readonly TimeSpan VerificationLifetime = TimeSpan.FromDays(29);

  public static Task<Dictionary<Guid, RoutePoint>> ResolveAsync(
    DispatchEntity load,
    IReadOnlyCollection<DispatchStop> stops,
    IAppDbContext db,
    IRoutingProvider routing,
    CancellationToken ct
  ) =>
    ResolveAsync(
      load.Id,
      load.ExecutionLegId,
      stops.Select(RouteWorkProjection.CaptureStop).ToArray(),
      db,
      routing,
      ct
    );

  public static async Task<Dictionary<Guid, RoutePoint>> ResolveAsync(
    Guid dispatchId,
    Guid? executionLegId,
    IReadOnlyCollection<RouteWorkStop> stops,
    IAppDbContext db,
    IRoutingProvider routing,
    CancellationToken ct
  )
  {
    var explicitPoints = new Dictionary<Guid, RoutePoint>();
    if (executionLegId is { } legId)
    {
      var ids = stops.Select(x => x.Id).ToArray();
      var visits = await db
        .ExecutionLegStops.AsNoTracking()
        .Where(visit =>
          visit.ExecutionLegId == legId
          && ids.Contains(visit.Id)
          && db.SwitchParticipants.Any(p =>
            p.DispatchId == dispatchId
            && !p.IsCancelled
            && (
              p.OutgoingLegId == legId && p.ReleaseVisitId == visit.Id
              || p.IncomingLegId == legId && p.ReceiveVisitId == visit.Id
            )
          )
        )
        .Select(x => new
        {
          x.Id,
          x.Latitude,
          x.Longitude,
        })
        .ToListAsync(ct);
      foreach (var visit in visits)
      {
        var stop = stops.Single(x => x.Id == visit.Id);
        if (
          visit.Latitude is null
          || visit.Longitude is null
          || stop.Latitude != visit.Latitude
          || stop.Longitude != visit.Longitude
        )
          throw new RoutePlanningException(
            "The transfer location changed. Refresh the route."
          );
        var point = new RoutePoint(
          (double)visit.Latitude.Value,
          (double)visit.Longitude.Value
        );
        if (!point.IsValid)
          throw new RoutePlanningException(
            "The transfer needs valid coordinates."
          );
        // Switch sites are explicit map locations, not imported street addresses.
        explicitPoints.Add(visit.Id, point);
      }
    }
    var result = new Dictionary<Guid, RoutePoint>();
    foreach (var stop in stops)
    {
      ct.ThrowIfCancellationRequested();
      result.Add(
        stop.Id,
        explicitPoints.TryGetValue(stop.Id, out var point)
          ? point
          : await ResolveAsync(stop, routing, ct)
      );
    }
    return result;
  }

  public static string Address(DispatchStop stop) =>
    Address(RouteWorkProjection.CaptureStop(stop));

  public static Task<RoutePoint> ResolveAsync(
    DispatchStop stop,
    IRoutingProvider routing,
    CancellationToken ct
  ) => ResolveAsync(RouteWorkProjection.CaptureStop(stop), routing, ct);

  public static bool RequiresAddressVerification(DispatchStop stop) =>
    RequiresAddressVerification(RouteWorkProjection.CaptureStop(stop));

  public static RoutePoint? ReliablePoint(DispatchStop stop, DateTime now) =>
    ReliablePoint(RouteWorkProjection.CaptureStop(stop), now);

  public static RoutePoint? VerifiedPoint(DispatchStop stop, DateTime now) =>
    VerifiedPoint(RouteWorkProjection.CaptureStop(stop), now);

  public static string Address(RouteWorkStop stop) =>
    string.Join(
      ", ",
      string.Join(
          ",",
          new[]
          {
            stop.Address,
            stop.City,
            stop.Province,
            stop.ZipCode,
            stop.Country,
          }
        )
        .Split(
          ',',
          StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
        )
        .Distinct(StringComparer.OrdinalIgnoreCase)
    );

  public static Task<RoutePoint> ResolveAsync(
    RouteWorkStop stop,
    IRoutingProvider routing,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    if (ReliablePoint(stop, DateTime.UtcNow) is { } reliable)
      return Task.FromResult(reliable);
    // Imported coordinates can identify a city rather than the stated delivery
    // address.
    if (!string.IsNullOrWhiteSpace(stop.Address))
      return routing.GeocodeAsync(Address(stop), ct);
    var imported = ImportedPoint(stop);
    return imported is not null
      ? Task.FromResult(imported)
      : throw new RoutePlanningException(
        $"Stop {stop.Sequence} needs a street address or exact coordinates."
      );
  }

  public static bool RequiresAddressVerification(RouteWorkStop stop)
  {
    if (string.IsNullOrWhiteSpace(stop.Address))
      return false;
    var premise = stop.Address.Split(',', 2)[0].Trim();
    return string.IsNullOrWhiteSpace(stop.City)
      || !string.Equals(
        premise,
        stop.City.Trim(),
        StringComparison.OrdinalIgnoreCase
      );
  }

  private static RoutePoint? ImportedPoint(RouteWorkStop stop)
  {
    var point =
      stop.Latitude.HasValue && stop.Longitude.HasValue
        ? new RoutePoint((double)stop.Latitude, (double)stop.Longitude)
        : null;
    return point?.IsValid == true ? point : null;
  }

  public static RoutePoint? ReliablePoint(RouteWorkStop stop, DateTime now)
  {
    if (VerifiedPoint(stop, now) is { } verified)
      return verified;
    var imported = ImportedPoint(stop);
    if (imported is null || !RequiresAddressVerification(stop))
      return imported;
    // Keep provider coordinates available while Google retries only when the
    // saved address is still the exact imported source. Local edits must be
    // verified before they can move a route.
    return stop.AddressRetryAfter > now && MatchesImportedSource(stop)
      ? imported
      : null;
  }

  private static bool MatchesImportedSource(RouteWorkStop stop)
  {
    try
    {
      var source = JsonSerializer.Deserialize<StopAddress>(
        stop.SourceAddressJson
      );
      return source is not null
        && Same(source.Address, stop.Address)
        && Same(source.City, stop.City)
        && Same(source.Province, stop.Province)
        && Same(source.Country, stop.Country)
        && Same(source.ZipCode, stop.ZipCode);
    }
    catch (JsonException)
    {
      return false;
    }

    static bool Same(string left, string right) =>
      string.Equals(
        left.Trim(),
        right.Trim(),
        StringComparison.OrdinalIgnoreCase
      );
  }

  public static RoutePoint? VerifiedPoint(RouteWorkStop stop, DateTime now)
  {
    if (
      string.IsNullOrWhiteSpace(stop.Address)
      || string.IsNullOrWhiteSpace(stop.SourceAddressJson)
      || stop.AddressVerifiedAt is not { } verified
      || verified > now
      || now - verified >= VerificationLifetime
      || stop.AddressRetryAfter.HasValue
      || stop.Latitude is null
      || stop.Longitude is null
    )
      return null;
    try
    {
      if (
        string.IsNullOrWhiteSpace(
          JsonSerializer
            .Deserialize<StopAddress>(stop.SourceAddressJson)
            ?.Address
        )
      )
        return null;
    }
    catch (JsonException)
    {
      return null;
    }
    var point = new RoutePoint((double)stop.Latitude, (double)stop.Longitude);
    return point.IsValid ? point : null;
  }
}
