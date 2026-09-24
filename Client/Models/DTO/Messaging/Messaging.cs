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
);

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

public sealed record MessageTemplateView(
  string Name,
  string Language,
  int Parameters,
  string Text
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
public sealed record ConversationContext(
  Guid? DriverId,
  string? DriverName,
  string State,
  IReadOnlyList<ContextTruck> Trucks,
  IReadOnlyList<ContextLoad> Loads
);

// Role: driver or co-driver on a live leg, or assigned in the fleet.
public sealed record ContextTruck(Guid Id, string Number, string Role);

public sealed record ContextLoad(
  Guid Id,
  int LoadNumber,
  string CustomerName,
  string? Status,
  IReadOnlyList<string> Places
);

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
  IReadOnlyList<ApprovedTemplateView> Templates
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
