using Client.Models.DTO.Planning;

namespace Client.Models.DTO.Fleet;

// api/fleet/trucks/{truckId}/duty-status: the assigned driver's duty
// reading, with or without a route; Duty is null when the server has no
// current, matching history for the driver.
public sealed record TruckDutyStatus(
  Guid TruckId,
  Guid? DriverId,
  DriverDutyStatus? Duty
);
