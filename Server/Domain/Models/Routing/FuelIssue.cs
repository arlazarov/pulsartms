namespace Domain.Models.Routing;

// Which planned fuel stops are the driver's for the shift they are on, and
// whether they have been handed over. Nothing here sends anything: a stop
// is prepared by the plan, and only a dispatcher's confirmation or a
// provider's acceptance says it was sent.
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

  // WhatsApp accepted it; its delivery is the message's own status.
  public const string WhatsApp = "whatsapp";
}

// A visit's hand-over as the plan reads it now: when and by whom it was
// sent, and whether what the plan says today still matches what was sent.
// Sent is not delivered, and delivered is not read.
public sealed record FuelSendStatus(
  DateTime SentAt,
  string? SentBy,
  string Channel,
  bool Changed
)
{
  // The provider's latest word on the message that carried it: accepted,
  // sent, delivered, read or failed. Null for a hand-over by hand.
  public string? Delivery { get; init; }
}

public sealed record FuelIssueLine(
  string VisitKey,
  string Text,
  bool Sent,
  bool Changed
)
{
  public string? Delivery { get; init; }
}

public static class FuelIssueChannelStates
{
  public const string NotConfigured = "notConfigured";
  public const string NoDriver = "noDriver";
  public const string NoNumber = "noNumber";

  // The driver has not written to the business number in the last 24
  // hours, so WhatsApp would not deliver a free-form message.
  public const string OutsideWindow = "outsideWindow";
  public const string Ready = "ready";
}

// Who a WhatsApp hand-over would go to, and whether it can go now.
public sealed record FuelIssueRecipient(
  Guid? DriverId,
  string? DriverName,
  string? WhatsAppPhone,
  string State,
  DateTime? WindowEndsAt
);

// The latest WhatsApp attempt for this plan version. Accepted is not
// delivered; unknown means nobody knows whether it went.
public sealed record FuelIssueMessageState(
  string Status,
  DateTime StatusAt,
  int? ErrorCode,
  bool NeedsConfirmation
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
  public bool AutomaticSending { get; init; }
  public FuelIssueRecipient? Recipient { get; init; }
  public FuelIssueMessageState? LastMessage { get; init; }
}

// Sending the previewed plan through WhatsApp. SendAgain is a dispatcher
// saying so after an attempt whose outcome is unknown.
public sealed record FuelIssueSendRequest(
  FuelIssueSentRequest Plan,
  bool SendAgain = false
);

public sealed record FuelIssueSentRequest(
  DateTime ExpectedCalculatedAt,
  IReadOnlyList<string> VisitKeys
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
}
