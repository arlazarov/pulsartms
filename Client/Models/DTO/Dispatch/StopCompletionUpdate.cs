namespace Client.Models.DTO.Dispatch;

public sealed record StopCompletionUpdate(
  DateTimeOffset? CompletedAt,
  long Revision,
  string CompletionIdentity
);

public sealed record StopCompletionState(
  DateTime? CompletedAt,
  Guid? CompletedBy,
  string? CompletedByName,
  DateTime? RecordedAt,
  long Revision,
  bool IsCompleted = false
);
