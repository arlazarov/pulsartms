namespace Client.Models.DTO.Execution;

public sealed record ExecutionAssignment(
  Guid TruckId,
  Guid? DriverId,
  Guid? TrailerId,
  Guid? CoDriverId = null
);

public sealed record SwitchResourceOption(Guid Id, string Name);

public sealed record SwitchVisitOption(
  Guid Id,
  int Sequence,
  string Job,
  string Name,
  string Address,
  decimal? Latitude,
  decimal? Longitude,
  bool IsCompleted
)
{
  public DateOnly? ScheduledDate { get; init; }
  public TimeOnly? ScheduledTime { get; init; }
  public DateOnly? ScheduledDate2 { get; init; }
  public TimeOnly? ScheduledTime2 { get; init; }
  public bool IsWindow { get; init; }
}

public sealed record SwitchLoadOption(
  Guid DispatchId,
  int LoadNumber,
  string SourceSignature,
  ExecutionAssignment? Outgoing,
  Guid? OutgoingLegId,
  long? ExpectedOutgoingRevision,
  IReadOnlyList<SwitchVisitOption> Visits
)
{
  public string? SourceReviewReason { get; init; }
}

public sealed record SwitchAssignmentOption(
  ExecutionAssignment Resources,
  string Truck,
  string Driver,
  string Trailer
);

public sealed record SwitchParticipantDetails(
  Guid Id,
  Guid DispatchId,
  int LoadNumber,
  string TransferKind,
  long Revision,
  Guid OutgoingLegId,
  long OutgoingRevision,
  Guid IncomingLegId,
  long IncomingRevision,
  SwitchAssignmentOption Outgoing,
  SwitchAssignmentOption Incoming,
  Guid ReleaseVisitId,
  Guid ReceiveVisitId,
  DateTime? PlannedReleaseAt,
  DateTime? PlannedReceiveAt,
  DateTime? ReleasedAt,
  DateTime? ReceivedAt,
  bool CanRelease,
  bool CanReceive
)
{
  public bool Released { get; init; }
  public bool Received { get; init; }
  public string? SourceReviewReason { get; init; }
  public Guid? SourceReviewExecutionLegId { get; init; }
}

public sealed record SwitchDetails(
  Guid Id,
  string Status,
  long Revision,
  string SiteName,
  decimal Latitude,
  decimal Longitude,
  bool CanCancel,
  IReadOnlyList<SwitchParticipantDetails> Participants
);

public sealed record SwitchWorkspace(
  IReadOnlyList<SwitchLoadOption> Loads,
  IReadOnlyList<SwitchResourceOption> Trucks,
  IReadOnlyList<SwitchResourceOption> Drivers,
  IReadOnlyList<SwitchResourceOption> Trailers,
  IReadOnlyList<SwitchDetails> Operations
);

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
  public string TransferKind { get; init; } = "drop_hook";
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

public sealed record SwitchPreviewLoad(
  Guid DispatchId,
  ExecutionAssignment Outgoing,
  ExecutionAssignment Incoming,
  IReadOnlyList<SwitchVisitOption> Before,
  IReadOnlyList<SwitchVisitOption> After
);

public sealed record SwitchPreview(
  bool CanPlan,
  IReadOnlyList<string> Problems,
  IReadOnlyList<SwitchPreviewLoad> Loads
);

public sealed record SwitchResult(Guid Id, string Status, long Revision);

public sealed record SwitchParticipantAction(
  Guid IdempotencyKey,
  Guid SwitchId,
  Guid ParticipantId,
  long OperationRevision,
  long ParticipantRevision,
  long LegRevision,
  DateTimeOffset? OccurredAt
);

public sealed record CancelSwitchRequest(Guid IdempotencyKey, long Revision);
