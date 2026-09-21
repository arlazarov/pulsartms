using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

public interface IFleetTelemetryProvider
{
  Task<IReadOnlyList<VehicleTelemetry>> GetVehicleTelemetryAsync(
    CancellationToken cancellationToken = default
  );
  Task<VehicleLocationStream> GetLocationStreamAsync(
    IReadOnlyCollection<string> vehicleIds,
    DateTime startTime,
    DateTime endTime,
    string? cursor = null,
    CancellationToken cancellationToken = default
  );
}
