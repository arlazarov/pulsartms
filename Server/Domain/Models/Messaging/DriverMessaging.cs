namespace Domain.Models.Messaging;

public static class DriverMessageChannels
{
  public const string WhatsApp = "whatsapp";
}

// Accepted means the provider took the message; it is not sent, delivered
// or read until the provider says so. Unknown means the call ended without
// an answer, so the message may or may not have gone.
public static class DriverMessageStatuses
{
  public const string Sending = "sending";
  public const string Unknown = "unknown";
  public const string Rejected = "rejected";
  public const string Accepted = "accepted";
  public const string Sent = "sent";
  public const string Delivered = "delivered";
  public const string Read = "read";
  public const string Failed = "failed";

  // Stopped before the provider was called, because the plan moved.
  public const string Withdrawn = "withdrawn";
}

public enum DriverMessageOutcome
{
  NotConfigured,
  Accepted,
  Rejected,
  Unknown,
}

public sealed record DriverMessageSendResult(
  DriverMessageOutcome Outcome,
  string? ProviderMessageId = null,
  int? ErrorCode = null
);

public sealed record DriverMessageStatusEvent(
  string ProviderMessageId,
  string Status,
  DateTime At,
  int? ErrorCode
);

// A message a driver wrote. Kind is text, file, or unsupported (the
// provider's type name is then the text); Text is the body or a file's
// caption, bounded.
public sealed record DriverMessageInboundEvent(string Phone, DateTime At)
{
  public string? ProviderMessageId { get; init; }
  public string Kind { get; init; } = "text";
  public string Text { get; init; } = "";
  public InboundMedia? Media { get; init; }
}

// Sha256 as the provider sent it (hex or base64); FileName only for
// documents.
public sealed record InboundMedia(
  string Id,
  string MimeType,
  string? Sha256,
  string? FileName
);

// A provider notification after its signature and sender were checked.
// Events for another business number are counted and dropped.
public sealed record DriverMessagingNotification(
  IReadOnlyList<DriverMessageStatusEvent> Statuses,
  IReadOnlyList<DriverMessageInboundEvent> Inbound,
  int OtherSenders
)
{
  // The business number these events were addressed to.
  public string BusinessNumberId { get; init; } = "";
}
