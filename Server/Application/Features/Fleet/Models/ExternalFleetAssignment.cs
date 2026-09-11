namespace Application.Features.Fleet.Models;

public class ExternalFleetAssignment
{
  public string DriverExternalId { get; set; } = string.Empty;
  public string VehicleExternalId { get; set; } = string.Empty;
  public string VehicleName { get; set; } = string.Empty;
  public string AssignmentType { get; set; } = string.Empty;
  public bool IsPassenger { get; set; }
  public DateTime StartTime { get; set; }
  public DateTime? EndTime { get; set; }
}
