using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Interfaces;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class TruckLocationStore(IAppDbContext db) : ITruckLocationStore
{
  // A position an hour old still says where a truck was left; one from
  // yesterday says nothing worth drawing. Each sensor keeps its own
  // observation time, so a stale fuel reading does not hide a fresh position.
  public static readonly TimeSpan Freshness = TimeSpan.FromHours(6);

  public async Task<IReadOnlyList<TruckLocation>> ReadAsync(
    CancellationToken ct
  )
  {
    var since = DateTime.UtcNow - Freshness;
    return await (
      from reading in db.TruckLocationReadings.AsNoTracking()
      where reading.ObservedAt > since
      join truck in db.Trucks.AsNoTracking() on reading.TruckId equals truck.Id
      select new TruckLocation
      {
        TruckId = reading.TruckId,
        TruckExternalId = truck.ExternalId,
        UnitNumber = truck.UnitNumber,
        DriverName = truck.Driver == null ? "" : truck.Driver.Name,
        TrailerNumber = reading.TrailerNumber,
        Latitude = reading.Latitude,
        Longitude = reading.Longitude,
        Speed = reading.Speed,
        Heading = reading.Heading,
        UpdatedAt = reading.ObservedAt,
        ObservedAt = reading.ObservedAt,
        FormattedLocation = reading.FormattedLocation,
        EngineState = reading.EngineState,
        FuelPercent = reading.FuelPercent,
        FuelUpdatedAt = reading.FuelObservedAt,
        OutsideTemperatureCelsius = reading.OutsideTemperatureCelsius,
        OutsideTemperatureUpdatedAt = reading.TemperatureObservedAt,
      }
    ).ToListAsync(ct);
  }

  // An observation never replaces a newer one, so a slower reader cannot undo
  // a fresher position, and a sensor the provider omitted keeps its last
  // value rather than being cleared.
  public async Task WriteAsync(
    IReadOnlyList<TruckLocation> trucks,
    CancellationToken ct
  )
  {
    if (trucks.Count == 0)
      return;
    var ids = trucks.Select(x => x.TruckId).ToArray();
    var existing = await db
      .TruckLocationReadings.Where(x => ids.Contains(x.TruckId))
      .ToDictionaryAsync(x => x.TruckId, ct);
    var now = DateTime.UtcNow;
    foreach (var truck in trucks)
    {
      if (!existing.TryGetValue(truck.TruckId, out var row))
      {
        row = new TruckLocationReading { TruckId = truck.TruckId };
        db.TruckLocationReadings.Add(row);
      }
      else if (row.ObservedAt >= truck.ObservedAt)
        continue;
      row.Latitude = truck.Latitude;
      row.Longitude = truck.Longitude;
      row.Speed = truck.Speed;
      row.Heading = truck.Heading;
      row.EngineState = truck.EngineState;
      row.FormattedLocation = truck.FormattedLocation;
      row.TrailerNumber = truck.TrailerNumber;
      row.ObservedAt = truck.ObservedAt;
      row.RecordedAt = now;
      if (
        truck.FuelUpdatedAt is { } fuelAt
        && fuelAt > (row.FuelObservedAt ?? DateTime.MinValue)
      )
      {
        row.FuelPercent = truck.FuelPercent;
        row.FuelObservedAt = fuelAt;
      }
      if (
        truck.OutsideTemperatureUpdatedAt is { } tempAt
        && tempAt > (row.TemperatureObservedAt ?? DateTime.MinValue)
      )
      {
        row.OutsideTemperatureCelsius = truck.OutsideTemperatureCelsius;
        row.TemperatureObservedAt = tempAt;
      }
    }
    await db.SaveChangesAsync(ct);
  }
}
