namespace Domain.Entities.Messaging;

// A message in a conversation. Inbound messages are keyed by the provider's
// id, so a notification delivered twice records one message. Outbound
// messages are committed before the provider is called, keyed by the
// dispatcher's retry key and attempt; their status only moves forward.
public sealed class ConversationMessage : BaseEntity, ICompanyOwned
{
  public Guid CompanyId { get; set; }

  public Guid ConversationId { get; set; }
  public string Channel { get; set; } = "";
  public string BusinessNumberId { get; set; } = "";
  public string Direction { get; set; } = "";
  public string Kind { get; set; } = "";
  public string Body { get; set; } = "";

  // For a template: its name, language and parameters as sent (JSON).
  public string? Template { get; set; }
  public Guid? AuthorId { get; set; }
  public Guid? ReplyToId { get; set; }
  public string? ProviderMessageId { get; set; }
  public Guid? IdempotencyKey { get; set; }
  public int Attempt { get; set; }
  public string Status { get; set; } = "";
  public DateTime StatusAt { get; set; }
  public int? ErrorCode { get; set; }

  // The outbox for an outbound message: each worker taking it increments
  // Fence and may only move it while the fence is still its own.
  public long Fence { get; set; }
  public DateTime? LeaseUntil { get; set; }

  // When the provider says the message was written; for ordering a thread.
  public DateTime SentAt { get; set; }
  public DateTime CreatedAt { get; set; }
}

public static class ConversationMessageKinds
{
  public const string Text = "text";
  public const string File = "file";
  public const string Template = "template";

  // A kind PulsR does not read yet (location, contact, sticker, reaction):
  // recorded so the thread shows something arrived, never guessed at.
  public const string Unsupported = "unsupported";
}

public static class OutboundStates
{
  // Committed, not yet handed to a sender.
  public const string Queued = "queued";
}

public static class MessageDirections
{
  public const string Inbound = "in";
  public const string Outbound = "out";
}
