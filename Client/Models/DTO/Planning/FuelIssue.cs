namespace Client.Models.DTO.Planning;

// The server's reading of what a driver has been handed of their fuel
// plan. The Client formats it and never decides it.
public sealed record FuelSendStatus(
  DateTime SentAt,
  string? SentBy,
  string Channel,
  bool Changed
);

public sealed record FuelIssueLine(
  string VisitKey,
  string Text,
  bool Sent,
  bool Changed
);

public sealed record FuelIssuePreview(
  Guid TruckId,
  DateTime PlanCalculatedAt,
  Guid? ExecutionLegId,
  long AssignmentRevision,
  string IssueState,
  DateTimeOffset? HorizonEndsAt,
  bool Critical,
  List<FuelIssueLine> Lines,
  string Message
)
{
  public bool AutomaticSending { get; init; }
}

public sealed record FuelIssueSentRequest(
  DateTime ExpectedCalculatedAt,
  List<string> VisitKeys
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
}
