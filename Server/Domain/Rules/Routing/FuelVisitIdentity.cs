using System.Globalization;
using Domain.Models.Routing;

namespace Domain.Rules.Routing;

// What a hand-over is about, and what it said.
//
// A visit is a station at a place in the work: the load and the stop it
// comes before. The same station before another stop is another visit, and
// one send never stands for both. The plan's own visit key numbers legs of
// the remaining itinerary, which shift as stops are passed, so it cannot be
// the identity of something sent yesterday.
public static class FuelVisitIdentity
{
  public static string Key(FuelPlanStop stop) =>
    Key(stop.StationId, stop.DispatchId, stop.BeforeStopId);

  public static string Key(Guid station, Guid dispatch, Guid beforeStop) =>
    $"{station:N}:{dispatch:N}:{beforeStop:N}";

  // What the driver was told to do there. A fill is "fill the tank"
  // whatever the gallons come to on the day, so the tank level moving does
  // not make a send look out of date; a stated amount is the amount.
  public static string Content(FuelPlanStop stop) =>
    stop.FillToTarget
      ? "full"
      : Math.Round(stop.BuyGallons, MidpointRounding.AwayFromZero)
        .ToString("0", CultureInfo.InvariantCulture);
}
