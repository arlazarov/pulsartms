using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface ITruckFuelPlanStore
{
  Task<TruckFuelPlanSnapshot?> ReadAsync(
    Guid truckId,
    bool includeRoute,
    CancellationToken ct
  );
  Task<bool> SaveAsync(TruckFuelPlanSnapshot snapshot, CancellationToken ct);

  // Null expects no saved plan; replacements require the same UTC revision at
  // database microsecond precision.
  Task<bool> ReplaceAsync(
    TruckFuelPlanSnapshot snapshot,
    DateTime? expectedCalculatedAt,
    CancellationToken ct
  );
}
