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

// The two facts a load's stops say, read in one pass (one ordering of
// the stops, nothing kept between calls):
// - cargo delivered: the last Delivery or Drop Off among the stops the
//   truck attends - so a trailer drop or a driver-only stop after it does
//   not hide it - is overridden done, or, unless overridden not done,
//   recorded, or confirmed by hand once the attended stops up to it are
//   done (a trailer still to drop is the truck's work, not the cargo's; a
//   delivery confirmed by hand while the pickup never happened is a
//   mistake). Multi-drop loads are delivered at their last delivery.
// - truck work finished: the cargo is delivered and every attended stop
//   after that delivery is done. Work without a delivery is not judged
//   finished here; for an execution leg its status decides.
public readonly record struct CompletionFacts(
  bool CargoDelivered,
  bool TruckWorkFinished
);

public static class WorkCompletion
{
  public static CompletionFacts Of(IEnumerable<CompletionStop> stops)
  {
    var attended = stops
      .Where(x => !x.DriverOnly)
      .OrderBy(x => x.Sequence)
      .ToList();
    var final = attended.FindLastIndex(x => x.IsDelivery);
    if (final < 0)
      return default;
    var delivery = attended[final];
    var delivered =
      delivery.CompletionOverride == true
      || delivery.CompletionOverride != false
        && (
          delivery.Recorded
          || delivery.ConfirmedByHand
            && attended.Take(final + 1).All(x => x.IsCompleted)
        );
    return new(
      delivered,
      delivered && attended.Skip(final + 1).All(x => x.IsCompleted)
    );
  }
}

public static class CargoDelivery
{
  public static bool IsDelivered(IEnumerable<CompletionStop> stops) =>
    WorkCompletion.Of(stops).CargoDelivered;
}

public static class TruckWorkCompletion
{
  public static bool IsFinished(IEnumerable<CompletionStop> stops) =>
    WorkCompletion.Of(stops).TruckWorkFinished;
}
