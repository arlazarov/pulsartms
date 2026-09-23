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

public sealed record InboxView(
  IReadOnlyList<ConversationSummary> Conversations,
  bool More
);

public sealed record AttachmentView(
  Guid Id,
  string Name,
  string Type,
  string State,
  bool Available,
  string? FailureReason
);

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

public sealed record ConversationView(
  ConversationSummary Summary,
  IReadOnlyList<MessageView> Messages,
  bool Older
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
