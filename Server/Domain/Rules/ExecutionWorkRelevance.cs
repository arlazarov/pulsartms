using Domain.Models.Routing;

namespace Domain.Rules;

// Whether a load is still work: something the truck is driving now or will
// drive. A load leaves this set when the source cancels it, when the
// truck's work on it is finished (TruckWorkCompletion - cargo delivered and
// any trailer drop after it done), or when its date has passed without it
// ever starting. It shares its reading of delivery with LoadCompletion
// (CargoDelivery); before stage 4b each read "delivered" its own way.
public static class ExecutionWorkRelevance
{
  public static bool IsCurrentOrUpcoming(
    RouteWorkSnapshot load,
    DateOnly date,
    bool includeOverdue,
    bool requiresActualReview = false
  )
  {
    // A load the source has cancelled is not work, whatever its execution
    // leg still says. The leg is left alone - a cancellation reversed in the
    // source brings the same trip back, with its accepted itinerary and its
    // recorded events - but until then the truck is not driving it. 11005
    // carried a cancelled load across the map because the leg it was
    // accepted into was still open, and nothing asked the load itself.
    if (load.Status is "cancelled" or "canceled")
      return false;
    if (load.ExecutionLegId.HasValue)
      return load.ExecutionStatus is "active" or "planned";
    if (load.Status.Equals("completed", StringComparison.OrdinalIgnoreCase))
      return false;
    if (requiresActualReview)
      return true;
    // Truck work, not cargo: a load whose cargo is delivered while the
    // truck still has a trailer to drop stays the truck's work until the
    // drop is done (TruckWorkCompletion).
    if (TruckWorkCompletion.IsFinished(load.Stops.Select(CompletionStop.From)))
      return false;
    // A missed appointment or UTC midnight does not complete an active load.
    if (HasStarted(load))
      return true;
    var end =
      load.DeliveryDate
      ?? load.Stops.LastOrDefault()?.ScheduledDate
      ?? load.ShipDate;
    return includeOverdue || end is null || end >= date;
  }

  // A missed appointment does not stop a load that is already moving.
  public static bool HasStarted(RouteWorkSnapshot load) =>
    load.ExecutionLegId.HasValue
      ? load.ExecutionStatus == "active"
      : load.Status.Equals("in_transit", StringComparison.OrdinalIgnoreCase)
        || load.Stops.Any(stop =>
          stop.PickedUpAt.HasValue || stop.ManualCompletedAt.HasValue
        );
}
