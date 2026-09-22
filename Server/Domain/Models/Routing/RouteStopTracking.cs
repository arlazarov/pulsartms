using System.Text.Json.Serialization;

namespace Domain.Models.Routing;

public sealed class RouteStopTracking
{
  public List<Guid> PassedStopIds { get; set; } = [];
  public Dictionary<Guid, DateTime> VisitedStops { get; set; } = [];
  public Guid? NextStopId { get; set; }
  public string NextStopLabel { get; set; } = "";
  public bool AllStopsPassed { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public RouteMovement? Movement { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public DateTime? LastObservationAt { get; set; }
  public DateTime? OffRouteSince { get; set; }
}
