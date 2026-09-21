using Domain.Policies;

namespace Domain.Rules.Eta;

// What a forecast assumed, said to the person reading it. An arrival time
// is only as good as what it took for granted - whether the hours history
// was verified, which rests it counted, what it allowed for a pickup - and
// a dispatcher promising a customer a time is owed all of it.
public static class EtaAssumptions
{
  public static List<string> Opening(
    HosTravelClock clock,
    EtaPlanningOptions planning
  )
  {
    return new List<string>
    {
      "Estimated using saved truck travel time; future traffic and border delays may differ.",
      clock.HistoryAvailable
        ? "Verified HOS history: eligible split rest and ongoing rest are considered; no exemption assumptions."
        : "HOS history incomplete: conservative full rests; no split credit.",
      clock.RecapVerified
        ? "Cycle starts from current ELD hours; recap uses reconciled home-day duty history."
      : clock.CycleFeasibility.Verified
        ? "Cycle starts from current ELD hours; unreconciled history does not add recap credits."
      : "Cycle history or the current ELD cycle is unavailable; cycle feasibility is unknown.",
      "Road ETA includes daily HOS and stop service but does not assume a cycle wait or restart. Cycle alternatives are conditional plans, not driver instructions.",
      "Equipment-operation waits conservatively consume duty time without rest credit; cargo service durations do not apply to equipment collection.",
      $"Planning: {planning.DrivingHoursPerShift}h driving per shift, {planning.PreTripMinutes}m PTI, one {planning.FuelStopMinutes}m fuel allowance per shift and a separate {planning.DailyBreakMinutes}m daily break. ELD limits can require stopping earlier.",
      $"Road travel times retain routing speed/traffic assumptions; planning speed is capped at {planning.PlanningSpeedCapMph} mph with {planning.TravelTimeBufferPercent}% extra travel-time allowance, not a live traffic prediction.",
      $"{planning.PickupMinutes} minutes at pickups and {planning.DeliveryMinutes} minutes at deliveries. Facility service and appointment waiting are planned sleeper time, not cycle duty or observed ELD status.",
      "Configured cycles are used when available. Missing history keeps conservative rest assumptions; cross-border history credits require verification.",
    };
  }

  // What only the walk itself can know: the rests and waits it actually
  // took on the way.
  public static void Closing(
    List<string> assumptions,
    HosTravelClock clock,
    bool offRoute
  )
  {
    if (clock.SplitRests > 0)
      assumptions.Add($"{clock.SplitRests} split rest(s) included.");
    if (clock.RecapWaits > 0)
      assumptions.Add(
        $"{clock.RecapWaits} wait(s) for returning cycle hours included."
      );
    if (clock.PlannedOffDutyWaitHours > 0)
      assumptions.Add(
        "Appointment waits are planned sleeper periods. Qualifying full daily rest is credited once; cycle rest remains a conditional alternative, not observed driver status."
      );
    if (clock.CompletedOngoingRest)
      assumptions.Add(
        "ETA assumes the current rest of at least 3 hours continues to a full 10-hour rest; cycle limits remain separate."
      );
    if (offRoute)
      assumptions.Add(
        "Off-route ETA includes a conservative return to the saved route while route refresh continues."
      );
  }
}
