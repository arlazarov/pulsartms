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

  // A departure from the road as RerouteDecision follows it, fix by fix:
  // when it was first seen, how many fixes of their own have seen it and
  // how many of those were twice the threshold away (each up to the number
  // that can confirm it), and when it was confirmed.
  public DateTime? OffRouteSince { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public int OffRouteFixes { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
  public int OffRouteFarFixes { get; set; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public DateTime? OffRouteConfirmedAt { get; set; }

  public void ClearDeviation()
  {
    OffRouteSince = null;
    OffRouteFixes = 0;
    OffRouteFarFixes = 0;
    OffRouteConfirmedAt = null;
  }
}
