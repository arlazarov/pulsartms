using Application.Features.Mileage.Models;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Services;

internal static class MileageMovementView
{
  public static MileageMovementRow From(Movement movement) =>
    new(
      movement.Id,
      movement.Revision,
      movement.TruckId,
      movement.DriverId,
      movement.CoDriverId,
      movement.TrailerId,
      movement.Purpose,
      movement.CargoState,
      movement.PreviousDispatchId,
      movement.NextDispatchId,
      movement.CarriedDispatchId,
      movement.AllocatedDispatchId,
      movement.AllocationTarget,
      movement.AllocationReason,
      movement.PolicyRevision,
      movement.ManualOverride,
      movement.Origin == "manual" ? "recorded-movement" : movement.Origin,
      movement.PlannedMiles,
      movement.ActualMiles,
      movement.PlannedAt,
      movement.ActualAt,
      true
    )
    {
      FromLocation = movement.FromLocation,
      ToLocation = movement.ToLocation,
      PlannedSource = movement.PlannedSource,
      PlannedSourceReference = movement.PlannedSourceReference,
      ActualSource = movement.ActualSource,
      ActualSourceReference = movement.ActualSourceReference,
      StartedAt = movement.StartedAt,
      EndedAt = movement.EndedAt,
      Origin = movement.Origin,
      CanEditDistance = movement.Origin == "manual",
    };

  public static async Task<IReadOnlyList<MileageMovementRow>> WithNumbersAsync(
    IAppDbContext db,
    IReadOnlyList<MileageMovementRow> rows,
    CancellationToken ct
  )
  {
    var ids = rows.SelectMany(row =>
        new[]
        {
          row.PreviousDispatchId,
          row.NextDispatchId,
          row.CarriedDispatchId,
          row.AllocatedDispatchId,
        }
      )
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    if (rows.Count == 0)
      return rows;
    var numbers = await db
      .Dispatches.AsNoTracking()
      .Where(x => ids.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LoadNumber, ct);
    int? Number(Guid? id) =>
      id.HasValue && numbers.TryGetValue(id.Value, out var value)
        ? value
        : null;
    var truckIds = rows.Select(x => x.TruckId)
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    var driverIds = rows.SelectMany(x => new[] { x.DriverId, x.CoDriverId })
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    var trailerIds = rows.Select(x => x.TrailerId)
      .OfType<Guid>()
      .Distinct()
      .ToArray();
    var trucks = await db
      .Trucks.AsNoTracking()
      .Where(x => truckIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.UnitNumber, ct);
    var drivers = await db
      .Drivers.AsNoTracking()
      .Where(x => driverIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    var trailers = await db
      .Trailers.AsNoTracking()
      .Where(x => trailerIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.UnitNumber, ct);
    return rows.Select(row =>
        row with
        {
          PreviousLoadNumber = Number(row.PreviousDispatchId),
          NextLoadNumber = Number(row.NextDispatchId),
          CarriedLoadNumber = Number(row.CarriedDispatchId),
          AllocatedLoadNumber = Number(row.AllocatedDispatchId),
          TruckNumber = row.TruckId is { } truck
            ? trucks.GetValueOrDefault(truck)
            : null,
          DriverName = row.DriverId is { } driver
            ? drivers.GetValueOrDefault(driver)
            : null,
          CoDriverName = row.CoDriverId is { } coDriver
            ? drivers.GetValueOrDefault(coDriver)
            : null,
          TrailerNumber = row.TrailerId is { } trailer
            ? trailers.GetValueOrDefault(trailer)
            : null,
        }
      )
      .ToArray();
  }
}

public static class MileageSummation
{
  public static MileageTotals Sum(
    IReadOnlyList<MileageMovementRow> rows,
    bool actual,
    bool truncated = false
  )
  {
    rows = rows.Where(row =>
        actual ? row.Origin != "native-route" : row.Origin != "samsara-obd"
      )
      .ToArray();
    decimal? Distance(MileageMovementRow row) =>
      actual ? row.ActualMiles : row.PlannedMiles;
    var missing = rows.Count(row => !Distance(row).HasValue);
    if (rows.Count == 0 || truncated)
      return new(null, null, null, null, null, missing);
    decimal? SumState(Func<MileageMovementRow, bool> predicate)
    {
      var matching = rows.Where(predicate).ToArray();
      return matching.Any(row => !Distance(row).HasValue)
        ? null
        : matching.Sum(row => Distance(row)!.Value);
    }
    return new(
      SumState(row => row.CargoState == "loaded"),
      SumState(row => row.CargoState is "empty" or "bobtail"),
      SumState(row => row.CargoState == "bobtail"),
      SumState(row => row.CargoState == "unknown"),
      missing == 0 ? rows.Sum(row => Distance(row)!.Value) : null,
      missing
    );
  }
}
