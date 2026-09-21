using System.Collections.Immutable;

namespace Domain.Models.Routing;

// How far ahead a fuel plan looks and what it looks over: the joined road,
// the stops along it, which loads they belong to, and what the answer
// depends on - so that a saved plan can be checked against the same facts
// later.
public sealed record FuelHorizonResult(
  TruckRoute Route,
  List<PlanStop> Stops,
  int CurrentStopCount,
  List<Guid> DispatchIds,
  string AssignmentSignature,
  List<string> Notes
)
{
  public List<FuelItineraryStop> Itinerary { get; init; } = [];
  public Dictionary<Guid, string> DispatchSignatures { get; init; } = [];
  public double StartAccessMiles { get; init; }
  public ImmutableArray<DeadheadHistoryBatch> History { get; init; } = [];
  public ImmutableArray<SavedRoadVersion> Roads { get; init; } = [];
}
