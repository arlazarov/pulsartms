using System.Collections.Immutable;

namespace Domain.Models.Execution;

public sealed record WorkIdentity(Guid DispatchId, Guid? ExecutionLegId);

public enum WorkPrecedenceBasis
{
  CurrentActivity,
  ScheduledStart,
  ExecutionLink,
}

public enum WorkSequenceProblem
{
  CompetingCurrentWork,
  UnknownOrder,
  ConflictingOrder,
  MissingExecutionLink,
  AwaitingTransfer,
}

public sealed record WorkPrecedence(
  WorkIdentity Previous,
  WorkIdentity Next,
  WorkPrecedenceBasis Basis
);

public sealed record WorkSequenceIssue(
  WorkIdentity Work,
  WorkSequenceProblem Problem
);

public sealed record WorkLegPosition(
  Guid DispatchId,
  Guid ExecutionLegId,
  int Sequence,
  Guid TruckId,
  string Status,
  long AssignmentRevision,
  Guid StartVisitId,
  Guid EndVisitId
);

public sealed record WorkTransferDependency(
  Guid Id,
  Guid DispatchId,
  Guid OutgoingLegId,
  Guid IncomingLegId,
  bool ReleaseConfirmed,
  bool ReceiptConfirmed,
  long Revision,
  Guid SwitchId,
  Guid ReleaseVisitId,
  Guid ReceiveVisitId,
  DateTime? PlannedReleaseAt,
  DateTime? PlannedReceiveAt,
  DateTime? ReleasedAt,
  DateTime? ReceivedAt
);

public sealed record WorkSequenceEvidence(
  ImmutableArray<WorkLegPosition> Legs,
  ImmutableArray<WorkTransferDependency> Transfers
)
{
  public static WorkSequenceEvidence Empty { get; } = new([], []);
}

public sealed record WorkSequenceAssessment(
  ImmutableArray<WorkPrecedence> Precedence,
  ImmutableArray<WorkSequenceIssue> Issues,
  ImmutableArray<WorkTransferDependency> Transfers
);
