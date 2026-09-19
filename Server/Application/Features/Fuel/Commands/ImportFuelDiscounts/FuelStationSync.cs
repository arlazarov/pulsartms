using Application.Features.Fuel.Exceptions;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Domain.Entities.Fuel;

namespace Application.Features.Fuel.Commands.ImportFuelDiscounts;

public static class FuelStationSync
{
  public static async Task<
    IReadOnlyDictionary<
      (string StationId, string Query),
      FuelStationLookupResult
    >
  > PrepareAsync(
    IAppDbContext dbContext,
    FuelStationLookupService lookups,
    IReadOnlyCollection<FuelDiscountImportRow> rows,
    CancellationToken cancellationToken = default
  )
  {
    if (
      rows.Any(row =>
        !string.IsNullOrWhiteSpace(row.StationId)
        && string.IsNullOrWhiteSpace(row.State)
      )
    )
      throw new InvalidOperationException(
        "Fuel station state/province is required before updating locations."
      );
    var ids = rows.Select(x => x.StationId)
      .Where(x => !string.IsNullOrWhiteSpace(x))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    var stations = await dbContext
      .FuelStations.AsNoTracking()
      .Where(x => ids.Contains(x.ExternalId))
      .ToDictionaryAsync(
        x => x.ExternalId,
        StringComparer.OrdinalIgnoreCase,
        cancellationToken
      );
    var results =
      new Dictionary<
        (string StationId, string Query),
        FuelStationLookupResult
      >();
    foreach (var row in rows)
    {
      if (string.IsNullOrWhiteSpace(row.StationId))
        continue;
      if (
        stations.TryGetValue(row.StationId, out var existing)
        && Matches(existing, row)
      )
        continue;
      var key = Key(row);
      if (!results.ContainsKey(key))
        results[key] = await lookups.PrepareAsync(
          row.StationId,
          Query(row),
          cancellationToken
        );
    }
    return results;
  }

  public static async Task SyncAsync(
    IAppDbContext dbContext,
    FuelStationLookupService lookups,
    IReadOnlyDictionary<
      (string StationId, string Query),
      FuelStationLookupResult
    > prepared,
    IReadOnlyCollection<FuelDiscountImportRow> rows,
    CancellationToken cancellationToken = default
  )
  {
    if (
      rows.Any(row =>
        !string.IsNullOrWhiteSpace(row.StationId)
        && string.IsNullOrWhiteSpace(row.State)
      )
    )
      throw new InvalidOperationException(
        "Fuel station state/province is required before updating locations."
      );
    var stationIds = rows.Select(x => x.StationId)
      .Where(x => !string.IsNullOrWhiteSpace(x))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    var stations = await dbContext
      .FuelStations.Where(x => stationIds.Contains(x.ExternalId))
      .ToListAsync(cancellationToken);
    var existing = stations.ToDictionary(
      x => x.ExternalId,
      StringComparer.OrdinalIgnoreCase
    );

    foreach (var row in rows)
    {
      if (string.IsNullOrWhiteSpace(row.StationId))
      {
        continue;
      }

      if (!existing.TryGetValue(row.StationId, out var station))
      {
        station = new FuelStation
        {
          Id = Guid.NewGuid(),
          ExternalId = row.StationId,
        };
        dbContext.FuelStations.Add(station);
        existing.Add(row.StationId, station);
      }
      if (Matches(station, row))
        continue;
      if (!prepared.TryGetValue(Key(row), out var result))
        throw new FuelStationLookupDeferredException();
      if (
        !await lookups.IsCurrentAsync(
          row.StationId,
          result.Revision,
          cancellationToken
        )
      )
        continue;
      var place = result.Place;
      station.Name = row.Name;
      station.City = row.City;
      station.Region = row.State;
      station.Address = place?.Address ?? "";
      station.Latitude = place?.Latitude;
      station.Longitude = place?.Longitude;
      // A station the provider reports as closed must not be planned into a
      // driver's route. Recorded here because this is where the provider is
      // already asked; stations that did not change are not asked again, so
      // this alone does not keep the answer current.
      if (place is not null)
      {
        station.PlaceId = place.PlaceId;
        station.BusinessStatus = place.BusinessStatus;
        station.OpeningHoursJson = place.OpeningHoursJson;
        station.UtcOffsetMinutes = place.UtcOffsetMinutes;
        station.StatusCheckedAt = DateTime.UtcNow;
      }
    }
  }

  private static string Query(FuelDiscountImportRow row) =>
    $"{row.Name}, {row.City}, {row.State}";

  private static (string, string) Key(FuelDiscountImportRow row) =>
    (
      row.StationId.Trim().ToUpperInvariant(),
      Query(row).Trim().ToUpperInvariant()
    );

  private static bool Matches(FuelStation station, FuelDiscountImportRow row) =>
    FuelStationLookupService.HasLocation(station.Latitude, station.Longitude)
    && string.Equals(
      $"{station.Name}, {station.City}, {station.Region}".Trim(),
      Query(row).Trim(),
      StringComparison.OrdinalIgnoreCase
    );
}
