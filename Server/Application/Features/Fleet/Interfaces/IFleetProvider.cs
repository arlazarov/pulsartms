using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

public interface IFleetProvider
{
  // A stable key for this provider's identities, recorded with the ids it
  // gives resources so another provider's ids are never read as its own.
  string Source { get; }

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
