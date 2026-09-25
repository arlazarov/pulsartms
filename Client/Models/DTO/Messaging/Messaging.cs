namespace Client.Models.DTO.Messaging;

public sealed record ConversationSummary(
  Guid Id,
  string Participant,
  Guid? DriverId,
  string? DriverName,
  string LastPreview,
  DateTime LastMessageAt,
  DateTime? LastInboundAt,
  bool WindowOpen,
  int Unread,
  string? ClaimedBy,
  DateTime? ClaimedUntil,
  long Revision
);

// Next continues the list below its last conversation, when there is more.
public sealed record InboxView(
  IReadOnlyList<ConversationSummary> Conversations,
  bool More
)
{
  public InboxCursor? Next { get; init; }
}

public sealed record InboxCursor(DateTime At, Guid Id);

public sealed record AttachmentView(
  Guid Id,
  string Name,
  string Type,
  string State,
  bool Available,
  string? FailureReason,
  IReadOnlyList<FiledView> Filed
);

// A load the file was filed to, as one of its documents.
public sealed record FiledView(Guid DispatchId, int LoadNumber, string Kind);

public sealed record MessageView(
  Guid Id,
  string Direction,
  string Kind,
  string Body,
  string Status,
  DateTime SentAt,
  string? Author,
  int? ErrorCode,
  IReadOnlyList<AttachmentView> Attachments
)
{
  // A later attempt of this reply exists; a server released before it
  // existed sends none.
  public bool Retried { get; init; }
}

// Next continues backwards; ReadThrough is the revision to mark read after
// showing this page, which never passes a driver message not yet shown.
// A server released before it existed sends none.
public sealed record ConversationView(
  ConversationSummary Summary,
  IReadOnlyList<MessageView> Messages,
  bool Older
)
{
  public MessageCursor? Next { get; init; }
  public long? ReadThrough { get; init; }

  // A window around a message has newer messages above it.
  public bool Newer { get; init; }
}

public sealed record MessageCursor(
  DateTime SentAt,
  DateTime CreatedAt,
  Guid Id
);

public sealed record SendMessageRequest(
  string Body,
  Guid IdempotencyKey,
  Guid? LastSeenMessageId,
  bool Confirm
);

public sealed record ReadRequest(long Revision);

// Purpose names the PulsR template this is, when it is recorded exactly
// as PulsR defines it ("contactRequest").
public sealed record MessageTemplateView(
  string Name,
  string Language,
  int Parameters,
  string Text,
  string? Purpose = null
);

public sealed record TemplateRequest(
  Guid IdempotencyKey,
  string Name,
  string Language,
  IReadOnlyList<string> Parameters
);

// How many conversations hold messages this dispatcher has not read (at
// most 99, then More), and the highest company arrival sequence among
// them: a notice is due only when it rises.
public sealed record UnreadCount(int Conversations, bool More, long Newest);

// Who the conversation is with and what they are driving. State:
// unmatched, no-truck, one-truck or several-trucks; loads are offered only
// for one truck.
// Hours: the driver's hours of service from the fleet's shared snapshot,
// null when no driver is linked.
public sealed record ConversationContext(
  Guid? DriverId,
  string? DriverName,
  string State,
  IReadOnlyList<ContextTruck> Trucks,
  IReadOnlyList<ContextLoad> Loads,
  ContextHours? Hours = null
);

// Known is false when the provider holds no clocks for this driver.
public sealed record ContextHours(
  bool Known,
  long? BreakMs,
  long? DriveMs,
  long? ShiftMs,
  long? CycleMs,
  DateTime? UpdatedAt,
  string? DutyStatus
);

// Role: driver or co-driver on a live leg, or assigned in the fleet.
public sealed record ContextTruck(Guid Id, string Number, string Role);

public sealed record ContextLoad(
  Guid Id,
  int LoadNumber,
  string CustomerName,
  string? Status,
  IReadOnlyList<string> Places
)
{
  public string OrderNumber { get; init; } = "";
  public IReadOnlyList<ContextStop> Stops { get; init; } = [];
}

public sealed record ContextStop(string Name, string City);

public sealed record DriverRequest(Guid? DriverId, long Revision);

public sealed record FileRequest(
  Guid? DispatchId,
  int? LoadNumber,
  string Kind
);

// Templates approved for the number the company sends from now; the number
// is null when WhatsApp is not set up.
public sealed record ApprovedTemplatesView(
  string? BusinessNumberId,
  IReadOnlyList<ApprovedTemplateView> Templates,
  IReadOnlyList<PulsrTemplateView>? Pulsr = null
);

// A template PulsR itself uses: what to submit to Meta, whether it is
// recorded for this number exactly so, and why PulsR cannot send it yet.
public sealed record PulsrTemplateView(
  string Purpose,
  string Name,
  string Language,
  int Parameters,
  string Body,
  string Submission,
  string? Unsupported,
  bool Recorded
);

public sealed record ApprovedTemplateView(
  Guid Id,
  string Name,
  string Language,
  int Parameters,
  string Text
);

public sealed record ApprovedTemplateRequest(
  string Name,
  string Language,
  int Parameters,
  string Text
);

// A driver a chat can be started with. Number is where their WhatsApp
// messages go - their WhatsApp number, else their phone (Source says which)
// - and null when their own WhatsApp number is not valid. ConversationId is
// the driver's conversation for that number, if any.
public sealed record MessagingDriver(
  Guid Id,
  string Name,
  string? Number,
  string Source,
  Guid? ConversationId
);

// Configured is false while the company has no WhatsApp number to send
// from; Next continues the list, by name, when there is more.
public sealed record MessagingDriversView(
  IReadOnlyList<MessagingDriver> Drivers,
  bool Configured,
  bool More
)
{
  public MessagingDriverCursor? Next { get; init; }
}

public sealed record MessagingDriverCursor(string Name, Guid Id);

// A message matching a search. FiledToLoad: a file from it was filed to
// the load searched for; MentionsLoad: its text names the load's number.
public sealed record MessageSearchHit(
  Guid MessageId,
  Guid ConversationId,
  string? DriverName,
  string Participant,
  string Direction,
  DateTime SentAt,
  string Snippet,
  bool FiledToLoad,
  bool MentionsLoad
);

public sealed record MessageSearchView(
  IReadOnlyList<MessageSearchHit> Hits,
  bool More
)
{
  public MessageCursor? Next { get; init; }
}

// One message to several drivers. Scope: all, group or selected (with
// DriverIds). A text, or an approved template with its parameters.
public sealed record BroadcastRequest(
  string Scope,
  IReadOnlyList<Guid>? DriverIds,
  string? Text,
  string? TemplateName,
  string? TemplateLanguage,
  IReadOnlyList<string>? Parameters
);

public sealed record BroadcastBody(
  Guid IdempotencyKey,
  BroadcastRequest Request
);

public sealed record BroadcastCandidate(
  Guid DriverId,
  string Name,
  string? Number,
  bool Eligible,
  string? Reason
);

public sealed record BroadcastPreview(
  IReadOnlyList<BroadcastCandidate> Recipients,
  int Eligible,
  string Body
);

public sealed record BroadcastRecipientView(
  Guid DriverId,
  string Name,
  Guid? ConversationId,
  Guid? MessageId,
  string Status,
  string? Reason
);

public sealed record BroadcastView(
  Guid Id,
  string Kind,
  string Body,
  string Scope,
  DateTime CreatedAt,
  DateTime? CancelledAt,
  IReadOnlyList<BroadcastRecipientView> Recipients
);
