namespace Client.Models.DTO.Dispatch;

public sealed record StopOperationUpdate(
  string? Action,
  string? StateAfter,
  long Revision,
  string StopIdentity
);

public sealed record StopOperationState(
  string? Action,
  string? StateAfter,
  long Revision,
  DateTime? RecordedAt
);
