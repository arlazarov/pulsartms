namespace Application.Features.Fleet.Models;

internal sealed record CameraRetrieval(
  Guid TruckId,
  string VehicleId,
  string ProviderId
);
