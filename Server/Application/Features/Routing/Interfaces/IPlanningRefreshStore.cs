using Domain.Models.Routing;

namespace Application.Features.Routing.Interfaces;

public interface IPlanningRefreshStore
{
  Task<PlanningRefreshState> RequestAsync(
    PlanningScope scope,
    string inputSignature,
    DateTime now,
    CancellationToken ct
  );

  Task<PlanningRefreshWork?> ClaimAsync(
    DateTime now,
    TimeSpan leaseDuration,
    CancellationToken ct
  );

  Task<bool> CompleteAsync(
    PlanningRefreshWork work,
    bool succeeded,
    DateTime now,
    DateTime nextAvailable,
    CancellationToken ct
  );

  // Unfinished demand requested at or before due, keyset-paged by Id, for
  // the consistency audit. Limit + 1 rows at most.
  Task<IReadOnlyList<PlanningRefreshOverdue>> OverdueAsync(
    DateTime due,
    string? after,
    int limit,
    CancellationToken ct
  );

  Task<bool> RequeueAsync(
    string id,
    long requestedVersion,
    DateTime now,
    CancellationToken ct
  );

  Task PruneAsync(DateTime before, CancellationToken ct);
}

public sealed record PlanningRefreshOverdue(
  string Id,
  Guid DispatchId,
  Guid? ExecutionLegId,
  long AssignmentRevision,
  long? CurrentLegRevision,
  long RequestedVersion,
  long CompletedVersion,
  DateTime RequestedAt,
  DateTime AvailableAt,
  int Attempts
);
