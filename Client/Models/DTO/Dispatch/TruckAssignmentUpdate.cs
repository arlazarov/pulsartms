namespace Client.Models.DTO.Dispatch;

public sealed record TruckAssignmentUpdate(
  string? TruckNumber,
  Guid? FromStopId,
  long Revision,
  string? StopIdentity
);

public sealed record TruckAssignmentState(
  Guid? TruckId,
  Guid? FromStopId,
  long Revision,
  DateTime? RecordedAt
);
