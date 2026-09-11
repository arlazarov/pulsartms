using System.Text.Json.Serialization;

namespace Infrastructure.Integrations.Samsara.Models;

public class SamsaraVehicleAssignment
{
  public SamsaraAssignmentReference Driver { get; set; } = new();
  public SamsaraAssignmentReference Vehicle { get; set; } = new();
  public string AssignmentType { get; set; } = string.Empty;
  public bool IsPassenger { get; set; }
  public DateTime StartTime { get; set; }
  [JsonConverter(typeof(OptionalSamsaraTimeConverter))]
  public DateTime? EndTime { get; set; }
}

public class SamsaraAssignmentReference
{
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
}

public class SamsaraTrailerAssignment
{
  public SamsaraTrailerAssignmentDriver Driver { get; set; } = new();
  public SamsaraTrailerAssignmentTrailer Trailer { get; set; } = new();
  public DateTime StartTime { get; set; }
  [JsonConverter(typeof(OptionalSamsaraTimeConverter))]
  public DateTime? EndTime { get; set; }
}

public class SamsaraTrailerAssignmentDriver
{
  public string DriverId { get; set; } = string.Empty;
}

public class SamsaraTrailerAssignmentTrailer
{
  public string TrailerId { get; set; } = string.Empty;
}
