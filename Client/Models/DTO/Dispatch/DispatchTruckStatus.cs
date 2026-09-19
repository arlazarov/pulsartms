namespace Client.Models.DTO.Dispatch;

public sealed record DispatchTruckStatus(
  Guid TruckId,
  decimal Speed,
  string EngineState,
  string TrailerNumber
);
