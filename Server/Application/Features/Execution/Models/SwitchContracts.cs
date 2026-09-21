namespace Application.Features.Execution.Models;

public sealed record SwitchLoadChange(
  Guid DispatchId,
  Guid? ReleaseVisitId,
  Guid? ReceiveVisitId,
  string SourceSignature,
  ExecutionAssignment Outgoing,
  ExecutionAssignment Incoming
)
{
  public Guid? SplitAfterVisitId { get; init; }
  public Guid? OutgoingLegId { get; init; }
  public long? ExpectedOutgoingRevision { get; init; }
  public Guid? OutgoingTripId { get; init; }
  public Guid? IncomingTripId { get; init; }
  public string TransferKind { get; init; } = "resource_handoff";
  public DateTimeOffset? PlannedReleaseAt { get; init; }
  public DateTimeOffset? PlannedReceiveAt { get; init; }
}

public sealed record PlanSwitchRequest(
  Guid IdempotencyKey,
  string SiteName,
  DateTimeOffset? PlannedAt,
  IReadOnlyList<SwitchLoadChange> Loads
)
{
  public decimal? Latitude { get; init; }
  public decimal? Longitude { get; init; }
  public bool ConfirmCompleted { get; init; }
}

public sealed record SwitchLegResult(
  Guid DispatchId,
  Guid OutgoingLegId,
  Guid IncomingLegId
)
{
  public Guid ParticipantId { get; init; }
  public Guid ReleaseVisitId { get; init; }
  public Guid ReceiveVisitId { get; init; }
  public long Revision { get; init; }
  public string TransferKind { get; init; } = "";
  public DateTime? ReleasedAt { get; init; }
  public DateTime? ReceivedAt { get; init; }
}

public sealed record SwitchResult(
  Guid Id,
  string Status,
  long Revision,
  IReadOnlyList<SwitchLegResult> Legs
);

public sealed record SwitchParticipantAction(
  Guid IdempotencyKey,
  Guid SwitchId,
  Guid ParticipantId,
  long OperationRevision,
  long ParticipantRevision,
  long LegRevision,
  DateTimeOffset? OccurredAt
);
