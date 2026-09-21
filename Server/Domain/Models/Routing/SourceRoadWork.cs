namespace Domain.Models.Routing;

public sealed record SourceRoadWork(
  Guid DispatchId,
  // Whose work this is. A worker claims whatever is next, then runs the
  // pass as the carrier it belongs to.
  Guid Company,
  Guid? TruckId,
  long Version,
  Guid LeaseId,
  DateTime LeaseUntil,
  int Attempts,
  bool Explicit
);
