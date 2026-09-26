using Application.Features.Fleet.Interfaces;

namespace Application.Features.Fleet.Models;

internal sealed record CameraRetrieval(
  Guid CompanyId,
  Guid TruckId,
  string VehicleId,
  CameraRequest Request
);
