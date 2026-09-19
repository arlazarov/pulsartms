using System.ComponentModel.DataAnnotations;

namespace Application.Features.Fuel.Options;

// Asking the place provider whether a station still exists costs money per
// request, and there are hundreds of stations, so the rate is a setting
// rather than a constant. The defaults sweep every station within about two
// days and then re-ask each one about twice a month.
public sealed class FuelStationStatusOptions
{
  public bool Enabled { get; set; } = true;

  [Range(1, 1440)]
  public int IntervalMinutes { get; set; } = 30;

  // How many stations one pass may ask about. The pass stops at this many
  // whether or not more are due, so a backlog drains at a known rate instead
  // of arriving as one bill.
  [Range(1, 200)]
  public int BatchSize { get; set; } = 15;

  // While stations remain that have never been asked about, passes follow
  // each other at this instead of the full interval. The first sweep of
  // several hundred, and any station added since, therefore finishes in
  // minutes rather than a day - the routine re-ask stays gentle.
  [Range(1, 600)]
  public int BacklogSeconds { get; set; } = 10;

  // How stale an answer may be before it is asked again, and therefore the
  // longest a station can be shut before this notices. A fortnight was the
  // first guess and it was too long: a travel stop that closes would be
  // planned into routes for two weeks before anyone found out. This applies
  // to the routine re-ask only - a station nobody has asked about yet, which
  // includes every newly imported one, is not made to wait for it.
  [Range(1, 365)]
  public int RecheckDays { get; set; } = 7;

  // Stations a saved plan is currently sending a truck to. These are few and
  // they are the ones a driver is about to arrive at, so they are asked about
  // on their own, far shorter, clock rather than waiting out a sweep of
  // several hundred.
  [Range(5, 1440)]
  public int PlannedRecheckMinutes { get; set; } = 60;
}
