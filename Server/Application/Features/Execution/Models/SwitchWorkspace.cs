namespace Domain.Models.Execution;

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

public sealed record CancelSwitchRequest(Guid IdempotencyKey, long Revision);
