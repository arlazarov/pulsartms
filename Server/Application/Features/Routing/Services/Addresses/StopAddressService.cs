using Application.Caching;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Domain.Entities.Dispatch;

namespace Application.Features.Routing.Services.Addresses;

public sealed class StopAddressService(
  IAppDbContext db,
  IAddressGeocoder geocoder,
  ReadCache cache,
  RoutePreparationQueue preparation
)
{
  public async Task VerifyAsync(
    IReadOnlyCollection<DispatchStop> stops,
    CancellationToken ct
  )
  {
    var candidates = stops
      .Where(x =>
        StopLocation.RequiresAddressVerification(x)
        && StopLocation.VerifiedPoint(x, DateTime.UtcNow) is null
      )
      .Select(x => x.Id)
      .ToArray();
    if (candidates.Length == 0)
      return;
    var parents = await db
      .Dispatches.AsNoTracking()
      .Where(x => x.Stops.Any(s => candidates.Contains(s.Id)))
      .Select(x => new { x.Id, x.TruckId })
      .ToDictionaryAsync(x => x.Id, x => x.TruckId, ct);
    foreach (var source in stops)
    {
      ct.ThrowIfCancellationRequested();
      if (StopLocation.VerifiedPoint(source, DateTime.UtcNow) is not null)
        continue;
      var stop = await db.DispatchStops.SingleOrDefaultAsync(
        x => x.Id == source.Id,
        ct
      );
      if (stop is null || string.IsNullOrWhiteSpace(stop.Address))
        continue;
      if (
        StopLocation.VerifiedPoint(stop, DateTime.UtcNow) is not null
        || stop.AddressRetryAfter > DateTime.UtcNow
      )
      {
        Copy(stop, source);
        continue;
      }
      await StopAddressResolution.ApplyAsync(
        stop,
        geocoder,
        DateTime.UtcNow,
        ct
      );
      try
      {
        await db.SaveChangesAsync(ct);
        Copy(stop, source);
        cache.Invalidate("dispatch");
        Notify([stop], parents);
      }
      catch (DbUpdateConcurrencyException)
      {
        // A synchronization changed the source while Google was resolving it.
        db.Entry(stop).State = EntityState.Detached;
      }
    }
  }

  public async Task ExpireAsync(CancellationToken ct)
  {
    var cutoff = DateTime.UtcNow - StopLocation.VerificationLifetime;
    var expired = await db
      .DispatchStops.Where(x => x.AddressVerifiedAt < cutoff)
      .OrderBy(x => x.AddressVerifiedAt)
      .ThenBy(x => x.Id)
      .Take(100)
      .ToListAsync(ct);
    if (expired.Count == 0)
      return;
    var ids = expired.Select(x => x.DispatchId).Distinct().ToArray();
    var parents = await db
      .Dispatches.AsNoTracking()
      .Where(x => ids.Contains(x.Id))
      .Select(x => new { x.Id, x.TruckId })
      .ToDictionaryAsync(x => x.Id, x => x.TruckId, ct);
    foreach (var stop in expired)
    {
      var source = StopAddressResolution.ReadSource(stop.SourceAddressJson);
      source?.Apply(stop);
      stop.AddressVerifiedAt = null;
      stop.AddressRetryAfter = source is null
        ? DateTime.UtcNow.AddHours(24)
        : null;
    }
    await db.SaveChangesAsync(ct);
    cache.Invalidate("dispatch");
    Notify(expired, parents);
  }

  private void Notify(
    IReadOnlyCollection<DispatchStop> stops,
    IReadOnlyDictionary<Guid, Guid?> parents
  )
  {
    var trucks = stops
      .SelectMany(x =>
        new[] { x.TruckId, parents.GetValueOrDefault(x.DispatchId) }
      )
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .Distinct()
      .ToArray();
    foreach (var truck in trucks)
      cache.InvalidateItem("planning-inputs", truck);
    preparation.AddressesChanged(
      stops.Select(x => x.DispatchId).Distinct().ToArray(),
      trucks
    );
  }

  private static void Copy(DispatchStop stop, DispatchStop source)
  {
    StopAddress.From(stop).Apply(source);
    source.Latitude = stop.Latitude;
    source.Longitude = stop.Longitude;
    source.SourceAddressJson = stop.SourceAddressJson;
    source.AddressVerifiedAt = stop.AddressVerifiedAt;
    source.AddressRetryAfter = stop.AddressRetryAfter;
  }
}
