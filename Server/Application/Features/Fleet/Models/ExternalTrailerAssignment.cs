namespace Application.Features.Fleet.Models;

public class ExternalTrailerAssignment
{
  public string DriverExternalId { get; set; } = string.Empty;
  public string TrailerExternalId { get; set; } = string.Empty;
  public DateTime StartTime { get; set; }
  public DateTime? EndTime { get; set; }
}
