using Application.Features.Routing.Models;

namespace Application.Features.Routing.Interfaces;

public interface ISourceRoadStore
{
  Task ObserveAsync(
    Guid dispatchId,
    Guid? truckId,
    string inputSignature,
    int priority,
    bool explicitlyRequested,
    bool refreshCompleted,
    DateTime now,
    CancellationToken ct
  );

  Task DemandAsync(
    Guid dispatchId,
    string identity,
    int priority,
    DateTime now,
    CancellationToken ct
  );

  Task<SourceRoadWork?> ClaimAsync(
    DateTime now,
    TimeSpan leaseDuration,
    CancellationToken ct
  );

  Task<bool> CompleteAsync(
    SourceRoadWork work,
    bool succeeded,
    DateTime now,
    DateTime nextAvailable,
    CancellationToken ct
  );

  Task PruneAsync(DateTime before, CancellationToken ct);
}
