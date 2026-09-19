namespace Application.Features.Routing.Models;

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

public sealed record RouteChoiceOption(
  int Number,
  TruckRoute Route,
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
  TruckRoute? SavedRoute
)
{
  public DateTime? OriginUpdatedAt { get; init; }
  public Guid? ExecutionLegId { get; init; }
}

public sealed record SavedRouteChoice(
  List<PlanStop> Stops,
  List<RouteViaPoint> ViaPoints,
  TruckRoute Route
)
{
  public SavedRemainingRoute? Remaining { get; init; }
}

public sealed record SavedRemainingRoute(
  List<PlanStop> Stops,
  TruckRoute Route
);

public sealed record RouteChoiceCurrentContext(
  List<PlanStop> FullStops,
  TruckRoute FullRoute,
  Guid PlanId,
  int PlanVersion,
  List<Guid> RemainingStopIds
);

public sealed record RouteChoiceWorkStamp(
  DateTimeOffset AsOf,
  string InputSignature
);

public sealed record RouteChoiceDraft(
  Guid Owner,
  string Inputs,
  string Profile,
  RouteChoicePreview Preview
)
{
  public RouteChoiceCurrentContext? Current { get; init; }
  public RouteChoiceWorkStamp? Work { get; init; }
}

public sealed record RouteChoiceDisplayRoute(
  DateTime CalculatedAt,
  double Miles,
  double Seconds,
  List<RouteLeg> Legs,
  List<string> Warnings
);

public sealed record RouteChoiceSummary(double Miles, double Seconds);

public sealed record RouteChoiceDisplayOption(
  int Number,
  RouteChoiceDisplayRoute Route,
  double DifferenceMiles,
  double DifferenceSeconds
);

public sealed record RouteChoiceDisplayPreview(
  Guid Id,
  Guid DispatchId,
  Guid TruckId,
  int LoadNumber,
  long Revision,
  DateTime ExpiresAt,
  List<PlanStop> Stops,
  List<RouteViaPoint> ViaPoints,
  List<RouteChoiceDisplayOption> Options,
  RouteChoiceSummary? SavedRoute
)
{
  public DateTime? OriginUpdatedAt { get; init; }
  public Guid? ExecutionLegId { get; init; }
}
