namespace Client.Models.DTO.Dispatch;

public sealed record DispatchActivityLink(Guid Id, string Label);

public sealed record DispatchActivityItem(
  Guid Id,
  Guid DispatchId,
  long CreatedRevision,
  long Revision,
  string Kind,
  string Text,
  Guid? StopId,
  string? StopLabel,
  Guid? DriverId,
  string? DriverName,
  Guid ActorId,
  string ActorName,
  DateTime RecordedAt,
  bool NeedsAttention,
  Guid? ResolvedBy,
  string? ResolvedByName,
  DateTime? ResolvedAt
);

public sealed record DispatchActivityPage(
  Guid DispatchId,
  long Revision,
  IReadOnlyList<DispatchActivityItem> Items,
  long? NextBeforeRevision,
  int OpenCount,
  IReadOnlyList<DispatchActivityItem> OpenItems,
  long? NextOpenBeforeRevision
);

public sealed record AddDispatchActivityUpdate(
  Guid OperationId,
  long ExpectedRevision,
  string Kind,
  string Text,
  Guid? StopId,
  Guid? DriverId,
  bool NeedsAttention
);

public sealed record ResolveDispatchActivityUpdate(
  Guid OperationId,
  long ExpectedRevision
);
