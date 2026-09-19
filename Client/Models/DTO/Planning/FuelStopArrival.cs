namespace Client.Models.DTO.Planning;

public sealed record FuelStopArrival(
  Guid DispatchId,
  Guid StopId,
  double Gallons,
  double Percent
);
