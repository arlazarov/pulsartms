using Domain.Models.Routing;

namespace Domain.Rules;

// One stop as the completion rules read it: its place, what it is, whether
// the truck attends it (a driver-only stop - "No truck" - it does not), a
// dispatcher's explicit override, whether its end was recorded (delivered
// or departed), whether it was confirmed by hand, and the stop's own
// completion.
public readonly record struct CompletionStop(
  int Sequence,
  string Job,
  bool DriverOnly,
  bool? CompletionOverride,
  bool Recorded,
  bool ConfirmedByHand,
  bool IsCompleted
)
{
  public bool IsDelivery =>
    Job.Equals("Drop Off", StringComparison.OrdinalIgnoreCase)
    || Job.Equals("Delivery", StringComparison.OrdinalIgnoreCase);

  public static CompletionStop From(RouteWorkStop stop) =>
    new(
      stop.Sequence,
      stop.Job,
      stop.StateAfter == "No truck",
      stop.CompletionOverride,
      stop.DeliveredAt is not null || stop.DepartedAt is not null,
      stop.ManualCompletedAt is not null,
      stop.IsCompleted
    );
}

// Whether the cargo was delivered: the load's final cargo delivery - the
// last Drop Off or Delivery among the stops the truck attends, so a trailer
// drop or a driver-only stop after it does not hide it - is overridden as
// done, or, unless overridden as not done, recorded, or confirmed by hand
// once every attended stop up to it is done - a trailer still to drop after
// it is the truck's work, not the cargo's. A delivery confirmed by hand
// while the pickup never happened is a mistake, not a delivery. Multi-drop
// loads are delivered at their last delivery.
public static class CargoDelivery
{
  public static bool IsDelivered(IEnumerable<CompletionStop> stops)
  {
    var attended = stops
      .Where(x => !x.DriverOnly)
      .OrderBy(x => x.Sequence)
      .ToList();
    return attended.LastOrDefault(x => x.IsDelivery) is { Job: not null } final
      && (
        final.CompletionOverride == true
        || final.CompletionOverride != false
          && (
            final.Recorded
            || final.ConfirmedByHand
              && attended
                .Where(x => x.Sequence <= final.Sequence)
                .All(x => x.IsCompleted)
          )
      );
  }
}

// Whether the truck's work on a load is finished: the cargo is delivered
// and every stop the truck attends after that delivery - a trailer drop -
// is done. Delivered cargo with a trailer still to drop is work the truck
// is finishing, not finished work. Work without a delivery is not judged
// finished here; for an execution leg its status decides.
public static class TruckWorkCompletion
{
  public static bool IsFinished(IEnumerable<CompletionStop> stops)
  {
    var attended = stops
      .Where(x => !x.DriverOnly)
      .OrderBy(x => x.Sequence)
      .ToList();
    return attended.LastOrDefault(x => x.IsDelivery) is { Job: not null } final
      && CargoDelivery.IsDelivered(attended)
      && attended
        .Where(x => x.Sequence > final.Sequence)
        .All(x => x.IsCompleted);
  }
}
