using Domain.Models.Routing;

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

  // A load's own saved plan (no execution leg) and each leg's, in one read
  // where the store can: work lists hold both kinds, and each read is a
  // round trip.
  async Task<SavedRoutePlanMetadataSet> ReadWorkAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    IReadOnlyCollection<Guid> executionLegIds,
    CancellationToken ct
  ) =>
    new(
      await ReadManyAsync(dispatchIds, ct),
      executionLegIds.Count == 0
        ? new Dictionary<Guid, SavedRoutePlanMetadata>()
        : await ReadExecutionLegsAsync(executionLegIds, ct)
    );
}

// Loads: by dispatch id, plans with no execution leg. Legs: by leg id.
public sealed record SavedRoutePlanMetadataSet(
  IReadOnlyDictionary<Guid, SavedRoutePlanMetadata> Loads,
  IReadOnlyDictionary<Guid, SavedRoutePlanMetadata> Legs
);
