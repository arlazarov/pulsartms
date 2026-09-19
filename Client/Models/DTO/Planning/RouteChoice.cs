using System.Text.Json.Serialization;

namespace Client.Models.DTO.Planning;

public sealed record RouteViaPoint(
  Guid Id,
  Guid BeforeStopId,
  string Label,
  RoutePoint Point
);

public sealed record RouteChoiceRequest(
  List<RouteViaPoint> ViaPoints,
  bool Alternatives = true,
  bool UseSavedVia = false,
  Guid? ExecutionLegId = null
);

public sealed record RouteChoiceSave(
  Guid PreviewId,
  int Option,
  long Revision,
  Guid? ExecutionLegId = null
);

public sealed class RouteChoiceDisplayRoute
{
  public DateTime CalculatedAt { get; set; }
  public double Miles { get; set; }
  public double Seconds { get; set; }
  public List<RouteLeg> Legs { get; set; } = [];
  public List<string> Warnings { get; set; } = [];
}

public sealed record RouteChoiceSummary(double Miles, double Seconds);

public sealed record RouteChoiceOption(
  int Number,
  RouteChoiceDisplayRoute Route,
  double DifferenceMiles,
  double DifferenceSeconds
);

public sealed record RouteChoicePreview(
  Guid Id,
  Guid DispatchId,
  Guid TruckId,
  int LoadNumber,
  long Revision,
  DateTime ExpiresAt,
  List<PlanStop> Stops,
  List<RouteViaPoint> ViaPoints,
  List<RouteChoiceOption> Options,
  RouteChoiceSummary? SavedRoute
)
{
  public DateTime? OriginUpdatedAt { get; init; }
  public Guid? ExecutionLegId { get; init; }
}

public sealed record RouteEditorMap(
  Guid Session,
  RouteChoicePreview Preview,
  int Selected,
  bool Editing,
  bool AddPoint
);

public sealed record RouteEditorUpdate(
  Guid Session,
  Guid PreviewId,
  int Selected,
  bool Editing,
  bool AddPoint,
  [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    RouteChoicePreview? Preview
);
