namespace Domain.Models.Routing;

// Which planned fuel stops are the driver's for the shift they are on, and
// whether they have been handed over. Nothing here sends anything: a stop
// is prepared by the plan, and only a dispatcher's confirmation (or, later,
// a transport's) says it was sent.
public static class FuelIssueHorizons
{
  // Reached before the current work period ends, plus the selection buffer.
  public const string Current = "current";

  // Beyond it, or with no arrival estimate: provisional, not for issuing.
  public const string Upcoming = "upcoming";
}

public static class FuelIssueStates
{
  // On duty or driving: the current-shift stops are ready to hand over.
  public const string Ready = "ready";

  // Off duty, in the sleeper or on personal conveyance - an approximation
  // of rest, not proof of sleep. Prepared, held until duty resumes.
  public const string AwaitingDuty = "awaitingDuty";

  // No fresh hours reading. The driver is not assumed to be awake, and no
  // stop is treated as the current shift.
  public const string HosUnknown = "hosUnknown";
}

public static class FuelSendChannels
{
  // A dispatcher passed the plan on by hand and said so.
  public const string Manual = "manual";
}

// A visit's hand-over as the plan reads it now: when and by whom it was
// sent, and whether what the plan says today still matches what was sent.
// Sent is not delivered, and delivered is not read.
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

// What a dispatcher would hand to the driver now: only the current-shift
// stops, in words taken from the itinerary, and the plan version they came
// from so a confirmation can be refused if the plan moved in between.
public sealed record FuelIssuePreview(
  Guid TruckId,
  DateTime PlanCalculatedAt,
  Guid? ExecutionLegId,
  long AssignmentRevision,
  string IssueState,
  DateTimeOffset? HorizonEndsAt,
  bool Critical,
  IReadOnlyList<FuelIssueLine> Lines,
  string Message
)
{
  // There is no transport yet. The preview is for copying by hand.
  public bool AutomaticSending { get; init; }
}

public sealed record FuelIssueSentRequest(
  DateTime ExpectedCalculatedAt,
  IReadOnlyList<string> VisitKeys
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
}
