using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

public interface IFleetProvider
{
  Task<IReadOnlyList<ExternalDriver>> GetDriversAsync(
    CancellationToken cancellationToken = default
  );
  Task<IReadOnlyList<ExternalVehicle>> GetVehiclesAsync(
    CancellationToken cancellationToken = default
  );
  Task<IReadOnlyList<ExternalTrailer>> GetTrailersAsync(
    CancellationToken cancellationToken = default
  );
  Task<IReadOnlyList<ExternalFleetAssignment>> GetAssignmentsAsync(
    DateTime startTime,
    DateTime endTime,
    CancellationToken cancellationToken = default
  );
  Task<IReadOnlyList<ExternalTrailerAssignment>> GetTrailerAssignmentsAsync(
    IReadOnlyCollection<string> driverIds,
    CancellationToken cancellationToken = default
  );
}
