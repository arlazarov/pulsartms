using Domain.Models.Routing;

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

  // The serving company's unfinished work last requested at or before due,
  // keyset-paged by dispatch, for the consistency audit. Limit + 1 rows at
  // most.
  Task<IReadOnlyList<SourceRoadOverdue>> OverdueAsync(
    DateTime due,
    Guid? after,
    int limit,
    CancellationToken ct
  );
}

public sealed record SourceRoadOverdue(
  Guid DispatchId,
  string? DispatchStatus,
  long RequestedVersion,
  long CompletedVersion,
  bool Explicit,
  DateTime RequestedAt,
  DateTime AvailableAt,
  int Attempts
);
