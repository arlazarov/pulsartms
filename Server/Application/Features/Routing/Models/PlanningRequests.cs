using System.Text.Json;
using System.Text.Json.Serialization;
using Domain.Models.Routing;

namespace Application.Features.Routing.Models;

// What callers send to plan a load's road and fuel. Request contracts, not
// the planning vocabulary: they belong to the commands that take them, so
// the web layer names Application alone (AGENTS.md).

public sealed record RouteBuildRequest(
  TruckRouteProfile Profile,
  bool FromCurrentPosition = false,
  int? NextStopSequence = null,
  Guid? ExecutionLegId = null
);

public sealed record FuelBuildRequest(
  TruckRouteProfile Profile,
  double? CurrentGallons = null
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
  public DateTime? AutomaticRefreshRevision { get; init; }
}

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

public sealed record FuelPlanEditRequest(
  DateTime? ExpectedCalculatedAt,
  List<FuelPlanEditStop>? Stops,
  int? QuantityStopIndex = null
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
}

// Sending the previewed plan through Messaging. SendAgain is a dispatcher
// saying so after an attempt whose outcome is unknown.
public sealed record FuelIssueSendRequest(
  FuelIssueSentRequest Plan,
  bool SendAgain = false
);

public sealed record FuelIssueSentRequest(
  DateTime ExpectedCalculatedAt,
  IReadOnlyList<string> VisitKeys
)
{
  public Guid? ExecutionLegId { get; init; }
  public long? AssignmentRevision { get; init; }
}

public sealed record PlanningSettingsUpdate(
  PlanningPreferences Preferences,
  long Revision
);

// A truck's route profile as a caller sends it: on the wire exactly the
// profile, read and written with the caller's own serializer options, so
// its defaults for missing fields are the profile's own.
[JsonConverter(typeof(TruckRouteProfileBodyConverter))]
public sealed record TruckRouteProfileBody(TruckRouteProfile Value);

public sealed class TruckRouteProfileBodyConverter
  : JsonConverter<TruckRouteProfileBody>
{
  public override TruckRouteProfileBody? Read(
    ref Utf8JsonReader reader,
    Type typeToConvert,
    JsonSerializerOptions options
  ) =>
    JsonSerializer.Deserialize<TruckRouteProfile>(ref reader, options)
      is { } profile
      ? new(profile)
      : null;

  public override void Write(
    Utf8JsonWriter writer,
    TruckRouteProfileBody value,
    JsonSerializerOptions options
  ) => JsonSerializer.Serialize(writer, value.Value, options);
}
