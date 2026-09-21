namespace Application.Features.Execution.Models;

public sealed record ExecutionSourceVisitChange(
  Guid VisitId,
  SwitchVisitOption Before,
  SwitchVisitOption After
);

public sealed record ExecutionSourceReview(
  Guid DispatchId,
  Guid ExecutionLegId,
  long AssignmentRevision,
  string SourceSignature,
  bool CanApply,
  IReadOnlyList<string> Problems,
  IReadOnlyList<ExecutionSourceVisitChange> Changes
);

public sealed record AcceptExecutionSourceChangesRequest(
  Guid ExecutionLegId,
  long ExpectedRevision,
  string SourceSignature,
  Guid IdempotencyKey
);

public sealed record ExecutionSourceApplyResult(
  Guid DispatchId,
  Guid ExecutionLegId,
  long AssignmentRevision
)
{
  public IReadOnlyList<ExecutionSourceVisitChange> Changes { get; init; } = [];
}
