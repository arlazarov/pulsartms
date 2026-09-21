using Domain.Models.Routing;

namespace Domain.Rules;

// Whether a load is still work: something the truck is driving now or will
// drive. A load leaves this set when the source cancels it, when its
// delivery is done, or when its date has passed without it ever starting.
//
// The delivery test here is a second reading of the question [[LoadCompletion]]
// answers, and it is not the same reading: this one looks back for the last
// delivery among the stops a truck attends, so a load whose final stop is a
// trailer drop still counts as delivered. LoadCompletion looks only at the
// last stop. Both are in use; neither was written knowing about the other.
public static class ExecutionWorkRelevance
{
  public static bool IsCurrentOrUpcoming(
    RouteWorkSnapshot load,
    DateOnly date,
    bool includeOverdue
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
    var final = load.Stops.LastOrDefault(x =>
      x.StateAfter != "No truck"
      && (
        x.Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
        || x.Job.Equals("Delivery", StringComparison.OrdinalIgnoreCase)
      )
    );
    if (
      final?.CompletionOverride == true
      || final?.CompletionOverride != false
        && (
          final?.DeliveredAt is not null
          || final?.DepartedAt is not null
          || final?.ManualCompletedAt is not null
            && load.Stops.Where(s => s.StateAfter != "No truck")
              .All(s => s.IsCompleted)
        )
    )
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
