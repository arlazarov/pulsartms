using System.Globalization;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// The words a driver is handed for this shift's fuel.
//
// Every place in them is a stop of the itinerary or the station itself;
// nothing is inferred. A stop on the road the truck is on now is placed by
// the distance to it. A later one is placed between the stops it falls
// between. No time is given: an estimate read out as a time is read as a
// promise.
public static class FuelIssueMessage
{
  private const double LitresPerGallon = 3.785411784;

  public static string Line(
    FuelPlanStop visit,
    IReadOnlyList<FuelItineraryStop> itinerary,
    Guid currentDispatch,
    Guid? nextStop
  )
  {
    var at = itinerary
      .Select((stop, index) => (stop, index))
      .Where(x =>
        x.stop.DispatchId == visit.DispatchId
        && x.stop.Stop.Id == visit.BeforeStopId
      )
      .Select(x => (int?)x.index)
      .FirstOrDefault();
    var next = itinerary
      .Select((stop, index) => (stop, index))
      .Where(x =>
        x.stop.DispatchId == currentDispatch && x.stop.Stop.Id == nextStop
      )
      .Select(x => (int?)x.index)
      .FirstOrDefault();
    var fuel = $"fuel at {Station(visit)} - {Quantity(visit)}.";
    if (at is not { } index)
      return $"{Capital(fuel)}";
    var before = itinerary[index].Stop;
    if (next == index)
      return $"Ahead, about {Miles(visit.MilesAhead)} before your "
        + $"{Job(before)} at {Place(before)}: {fuel}";
    if (index > 0 && (next is null || index > next))
    {
      var after = itinerary[index - 1].Stop;
      return $"After your {Job(after)} at {Place(after)}, on the way to "
        + $"your {Job(before)} at {Place(before)}: {fuel}";
    }
    return $"Before your {Job(before)} at {Place(before)}: {fuel}";
  }

  public static string Compose(IReadOnlyList<string> lines) =>
    lines.Count == 0
      ? "No fuel stop is planned for this shift."
      : "Fuel for this shift:\n"
        + string.Join(
          "\n",
          lines.Select(
            (line, index) =>
              $"{(index + 1).ToString(CultureInfo.InvariantCulture)}. {line}"
          )
        );

  private static string Station(FuelPlanStop visit) =>
    string.IsNullOrWhiteSpace(visit.Address)
      ? visit.Name
      : $"{visit.Name}, {visit.Address}";

  private static string Quantity(FuelPlanStop visit)
  {
    if (visit.FillToTarget)
      return "fill the tank";
    var gallons = Math.Round(visit.BuyGallons, MidpointRounding.AwayFromZero);
    var text = $"{gallons.ToString("0", CultureInfo.InvariantCulture)} gal";
    // A pump that sells litres is told in litres as well; the plan's own
    // quantity stays in gallons.
    if (
      visit.Unit.Contains('L', StringComparison.Ordinal)
      && !visit.Unit.Contains("gal", StringComparison.OrdinalIgnoreCase)
    )
      text +=
        " (about "
        + Math.Round(visit.BuyGallons * LitresPerGallon)
          .ToString("0", CultureInfo.InvariantCulture)
        + " L)";
    return text;
  }

  private static string Miles(double miles) =>
    Math.Max(1, Math.Round(miles, MidpointRounding.AwayFromZero))
      .ToString("0", CultureInfo.InvariantCulture) + " mi";

  private static string Job(PlanStop stop) =>
    string.IsNullOrWhiteSpace(stop.Job)
      ? "stop"
      : stop.Job.Trim().ToLowerInvariant();

  private static string Place(PlanStop stop) =>
    string.IsNullOrWhiteSpace(stop.Name) ? stop.Address : stop.Name;

  private static string Capital(string text) =>
    text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
