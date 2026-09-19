using Application.Features.Execution.Interfaces;
using Application.Features.Execution.Services;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Routes;

namespace Application.Features.Routing.Services.FuelPlanning;

public sealed class FuelWorkInputsReader(
  TruckItineraryReader itineraries,
  IExecutionReadScope scope,
  TruckPlanningInputsReader displays
) : IFuelWorkInputsReader
{
  public async Task<FuelWorkInputs> ReadFreshAsync(
    Guid truckId,
    CancellationToken ct
  )
  {
    var snapshot = await scope.ReadAsync(
      token => itineraries.ReadAsync(truckId, DateTimeOffset.UtcNow, token),
      ct,
      requireFreshSnapshot: true
    );
    return snapshot is null
      ? throw new RoutePlanningException(
        "Truck assignments could not be verified. The saved fuel plan has been kept."
      )
      : new(snapshot);
  }

  public async Task<FuelWorkInputs?> ReadDisplayAsync(
    Guid truckId,
    CancellationToken ct
  ) =>
    await displays.ReadAsync(truckId, ct, includeHos: false) is { } captured
      ? new(captured.Itinerary)
      : null;
}
