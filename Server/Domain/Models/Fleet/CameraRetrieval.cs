namespace Domain.Models.Fleet;

public sealed record CameraRetrieval(
  Guid TruckId,
  string VehicleId,
  string ProviderId
);
