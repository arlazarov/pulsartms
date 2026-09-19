namespace Application.Features.Routing.Models;

public sealed record SourceRoadWork(
  Guid DispatchId,
  Guid? TruckId,
  long Version,
  Guid LeaseId,
  DateTime LeaseUntil,
  int Attempts,
  bool Explicit
);
