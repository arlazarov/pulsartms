using Domain.Models.Fleet;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// Which of a plan's stops belong to the shift the driver is on.
//
// The whole plan is still optimised over the long horizon - that is what
// decides today's quantities. This only draws the line between the stops
// to hand over now and the ones that stay provisional. The line is the end
// of the driver's current work period, read from their hours, plus a small
// selection buffer; it is not calendar midnight. Each stop's arrival is
// already an hours-aware estimate: a stop past a required rest arrives
// after that rest, and so falls outside the window.
public static class FuelIssueHorizon
{
  public static void Apply(
    FuelPlan plan,
    DriverHosClocks? clocks,
    DateTimeOffset now,
    TimeSpan buffer,
    TimeSpan freshness
  )
  {
    var end = WorkPeriodEnd(clocks, now, freshness);
    plan.IssueState = State(clocks, end);
    plan.IssueHorizonEndsAt = end + buffer;
    var inside = end is { } until;
    foreach (var stop in plan.Stops)
    {
      // Once one stop is past the line, every later one is too.
      inside =
        inside
        && stop.EstimatedArrival is { } arrival
        && arrival <= end!.Value + buffer;
      stop.IssueHorizon = inside
        ? FuelIssueHorizons.Current
        : FuelIssueHorizons.Upcoming;
    }
    // A stop the tank no longer reaches is not a matter for the driver's
    // next message; it is for the dispatcher, now.
    plan.IssueCritical = plan.Stops.Any(stop =>
      stop.ArrivalGallons < FuelReservePolicy.PhysicalArrivalMinimumGallons
    );
  }

  // The end of the on-duty window, as a wall-clock time. Without a fresh
  // reading there is no current work period to speak of.
  public static DateTimeOffset? WorkPeriodEnd(
    DriverHosClocks? clocks,
    DateTimeOffset now,
    TimeSpan freshness
  )
  {
    if (
      clocks is null
      || string.IsNullOrEmpty(clocks.CurrentDutyStatus)
      || clocks.UpdatedAt == default
    )
      return null;
    var read = new DateTimeOffset(
      DateTime.SpecifyKind(clocks.UpdatedAt, DateTimeKind.Utc)
    );
    if (read > now + TimeSpan.FromMinutes(1) || read < now - freshness)
      return null;
    // The shift window is wall-clock. Driving time is not, and is used only
    // when the window is unknown, as the shorter and so safer bound.
    var left = clocks.ShiftMs ?? clocks.DriveMs;
    return left is >= 0 ? read + TimeSpan.FromMilliseconds(left.Value) : null;
  }

  private static string State(DriverHosClocks? clocks, DateTimeOffset? end) =>
    end is null ? FuelIssueStates.HosUnknown
    : clocks!.CurrentDutyStatus is "driving" or "onDuty" or "yardMove"
      ? FuelIssueStates.Ready
    // Personal conveyance is off-duty movement: it is not working time.
    : clocks.CurrentDutyStatus
      is "offDuty"
        or "sleeperBerth"
        or "personalConveyance"
      ? FuelIssueStates.AwaitingDuty
    : FuelIssueStates.HosUnknown;
}
