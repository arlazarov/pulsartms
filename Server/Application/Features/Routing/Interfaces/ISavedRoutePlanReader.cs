using Application.Features.Routing.Models;

namespace Application.Features.Routing.Interfaces;

public interface ISavedRoutePlanReader
{
  Task<SavedRoutePlanMetadata?> ReadAsync(
    Guid dispatchId,
    CancellationToken ct
  );
  Task<IReadOnlyDictionary<Guid, SavedRoutePlanMetadata>> ReadManyAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    CancellationToken ct
  );

  Task<SavedRoutePlanMetadata?> ReadExecutionLegAsync(
    Guid executionLegId,
    CancellationToken ct
  ) => throw new NotSupportedException("Execution-leg routes are unsupported.");

  Task<
    IReadOnlyDictionary<Guid, SavedRoutePlanMetadata>
  > ReadExecutionLegsAsync(
    IReadOnlyCollection<Guid> executionLegIds,
    CancellationToken ct
  ) => throw new NotSupportedException("Execution-leg routes are unsupported.");
}
