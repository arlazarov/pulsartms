namespace Domain.Models.Routing;

public sealed record FuelStopArrival(
  Guid DispatchId,
  Guid StopId,
  double Gallons,
  double Percent
);
