using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Services;

public sealed record MileageSegmentScope(
  ExecutionLeg Leg,
  Guid FromVisitId,
  Guid ToVisitId,
  string FromLocation,
  string ToLocation,
  string CargoState,
  string Purpose,
  Guid? PreviousDispatchId,
  Guid? NextDispatchId,
  Guid? CarriedDispatchId
)
{
  public Guid? DriverId { get; init; } = Leg.DriverId;
  public Guid? CoDriverId { get; init; } = Leg.CoDriverId;

  public static MileageSegmentScope Create(
    ExecutionLeg leg,
    IReadOnlyList<DispatchStop> stops,
    int index
  )
  {
    var from = stops[index];
    var to = stops[index + 1];
    var state = from.StateAfter.ToLowerInvariant();
    if (
      !MileageAllocation.ValidCargoState(state)
      || state == "bobtail" && leg.TrailerId.HasValue
      || state is "loaded" or "empty" && leg.TrailerId is null
    )
      state = "unknown";
    var indexes = stops
      .Select((stop, order) => (stop.Id, order))
      .ToDictionary(x => x.Id, x => x.order);
    var carried = leg
      .Loads.Where(x =>
        indexes.TryGetValue(x.StartVisitId, out var start)
        && indexes.TryGetValue(x.EndVisitId, out var end)
        && start <= index
        && end > index
      )
      .Select(x => x.DispatchId)
      .Distinct()
      .Take(2)
      .ToArray();
    var pickup = to.Job is "Pick Up" or "Pickup";
    var next =
      pickup && leg.Loads.Any(x => x.DispatchId == to.DispatchId)
        ? to.DispatchId
        : (Guid?)null;
    var previous =
      from.Job is "Drop Off" or "Delivery"
      && leg.Loads.Any(x => x.DispatchId == from.DispatchId)
        ? from.DispatchId
        : (Guid?)null;
    return new(
      leg,
      from.Id,
      to.Id,
      from.Address,
      to.Address,
      state,
      state == "loaded" ? "delivery"
        : pickup ? "pickup-approach"
        : "reposition",
      previous,
      next,
      state == "loaded" && carried.Length == 1 ? carried[0] : null
    )
    {
      DriverId = from.HasDriverOverride ? from.DriverId : leg.DriverId,
      CoDriverId = from.HasDriverOverride ? from.CoDriverId : leg.CoDriverId,
    };
  }

  public string Identity(string origin) =>
    $"{origin}:{Leg.Id}:{FromVisitId}:{ToVisitId}:{Leg.TruckId}:"
    + $"{DriverId}:{CoDriverId}:{Leg.TrailerId}:{CargoState}:"
    + $"{Purpose}:{PreviousDispatchId}:{NextDispatchId}:{CarriedDispatchId}";

  public Movement Movement(
    string origin,
    Guid key,
    string requestHash,
    DateTime now,
    DateTime? start = null,
    DateTime? end = null
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = key,
      RequestHash = requestHash,
      Origin = origin,
      ExecutionLegId = Leg.Id,
      TruckId = Leg.TruckId,
      DriverId = DriverId,
      CoDriverId = CoDriverId,
      TrailerId = Leg.TrailerId,
      FromVisitId = FromVisitId,
      ToVisitId = ToVisitId,
      FromLocation =
        FromLocation.Length <= 500 ? FromLocation : FromLocation[..500],
      ToLocation = ToLocation.Length <= 500 ? ToLocation : ToLocation[..500],
      Purpose = Purpose,
      CargoState = CargoState,
      PreviousDispatchId = PreviousDispatchId,
      NextDispatchId = NextDispatchId,
      CarriedDispatchId = CarriedDispatchId,
      StartedAt = start,
      EndedAt = end,
      Revision = 1,
      RecordedAt = now,
      RecordedBy = Guid.Empty,
    };
}
