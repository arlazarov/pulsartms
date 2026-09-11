namespace Application.Features.Routing.Models;

public sealed record FuelStopArrival(Guid DispatchId, Guid StopId, double Gallons, double Percent);
