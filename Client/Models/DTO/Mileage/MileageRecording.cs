namespace Client.Models.DTO.Mileage;

public sealed record RecordMovementRequest(
  Guid IdempotencyKey,
  Guid TruckId,
  Guid? DriverId,
  Guid? CoDriverId,
  Guid? TrailerId,
  Guid? ExecutionLegId,
  string Purpose,
  string CargoState,
  Guid? PreviousDispatchId,
  Guid? NextDispatchId,
  Guid? CarriedDispatchId,
  string FromLocation,
  string ToLocation,
  DateTimeOffset? StartedAt,
  DateTimeOffset? EndedAt
);

public sealed record MovementDistanceUpdate(
  long Revision,
  string Basis,
  decimal Miles,
  string Source,
  string SourceReference,
  DateTimeOffset ObservedAt,
  string Reason,
  DateTimeOffset? StartedAt = null,
  DateTimeOffset? EndedAt = null
);

public sealed record MileageFleetList<T>(int TotalCount, List<T> Items);

public sealed record MileageUnitOption(
  Guid Id,
  string UnitNumber,
  bool IsActive
);

public sealed record MileageDriverOption(Guid Id, string Name, bool IsActive);
