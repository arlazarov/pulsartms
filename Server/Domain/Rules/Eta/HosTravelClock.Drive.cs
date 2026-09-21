using Domain.Models.Eta;

namespace Domain.Rules.Eta;

// Moving the clock forward: driving, working a stop, waiting for an
// appointment, resting at one. Each of these spends the driver's day, and
// each one takes whatever rest the rules require before it can continue -
// so a forecast never drives an hour the driver does not have.
public sealed partial class HosTravelClock
{
  public void Drive(double hours, string region)
  {
    if (!double.IsFinite(hours) || hours < 0 || hours > 720)
      throw new ArgumentOutOfRangeException(nameof(hours));
    Enter(region);
    var iterations = 0;
    while (hours > .000001)
    {
      if (++iterations > 1000)
        throw new InvalidOperationException(
          "ETA simulation exceeded its horizon."
        );
      RefreshCycle();
      if (
        cycleMode is HosCycleMode.Recap or HosCycleMode.Restart
        && !CycleFeasibility.Verified
      )
        throw new CycleScenarioUnavailableException();
      if (cycleMode != HosCycleMode.Observe && cycleLeft <= .000001)
      {
        if (cycleMode == HosCycleMode.Recap)
        {
          if (!WaitForScenarioRecap())
            throw new CycleScenarioUnavailableException();
        }
        else
          Reset(true);
        continue;
      }
      if (plannedDriveLeft <= .000001)
      {
        Reset(false, fullDailyRest: true);
        continue;
      }
      if (driveLeft <= .000001 || shiftLeft <= .000001)
      {
        Reset(false);
        continue;
      }
      if (preTripPending)
      {
        preTripPending = false;
        var preTrip = planning!.PreTripMinutes / 60d;
        PreTripHours += preTrip;
        Service(preTrip);
        continue;
      }
      if (FuelOwed)
      {
        Fuel();
        continue;
      }
      if (
        planning is not null
        && !plannedBreakTaken
        && (
          plannedBreakLeft <= .000001 || country == "US" && breakLeft <= .000001
        )
      )
      {
        CompletePlannedBreak();
        continue;
      }
      if (country == "US" && breakLeft <= .000001)
      {
        Rest(.5);
        shiftLeft -= .5;
        shiftDuty += .5;
        breakLeft = 8;
        sinceBreak = 0;
        continue;
      }
      var amount = Math.Min(hours, Math.Min(driveLeft, shiftLeft));
      if (cycleMode != HosCycleMode.Observe)
        amount = Math.Min(amount, cycleLeft);
      if (
        cycleMode is HosCycleMode.Recap or HosCycleMode.Restart
        && CycleFeasibility.NextHomeDay(Now) is { } homeDay
      )
        amount = Math.Min(amount, (homeDay - Now).TotalHours);
      amount = Math.Min(amount, plannedDriveLeft);
      if (planning is not null && !plannedBreakTaken)
        amount = Math.Min(amount, plannedBreakLeft);
      if (country == "US")
        amount = Math.Min(amount, breakLeft);
      var start = Now;
      Now = Now.AddHours(amount);
      timeline?.Add(start, Now, "driving");
      DriveHours += amount;
      hours -= amount;
      CycleFeasibility.Observe(start, Now, "driving");
      CheckScenarioHorizon();
      plannedRestStarted = null;
      driveLeft -= amount;
      shiftLeft -= amount;
      cycleLeft -= amount;
      breakLeft -= amount;
      shiftDrive += amount;
      shiftDuty += amount;
      sinceBreak += amount;
      plannedDriveLeft -= amount;
      plannedBreakLeft -= amount;
    }
  }

  public void Service(double hours)
  {
    if (hours == 0)
      return;
    plannedRestStarted = null;
    var start = Now;
    Now = Now.AddHours(hours);
    timeline?.Add(start, Now, "onDuty");
    shiftLeft -= hours;
    cycleLeft -= hours;
    shiftDuty += hours;
    CycleFeasibility.Observe(start, Now, "onDuty");
    CheckScenarioHorizon();
    // On-duty non-driving >=30m qualifies as a US break, not a daily reset.
    if (hours >= .5)
    {
      breakLeft = 8;
      sinceBreak = 0;
    }
  }

  public void WaitUntil(DateTimeOffset start)
  {
    if (start <= Now)
      return;
    var hours = (start - Now).TotalHours;
    PlannedOffDutyWaitHours += hours;
    StopRest(hours);
  }

  public void StopRest(double hours)
  {
    if (!double.IsFinite(hours) || hours < 0)
      throw new ArgumentOutOfRangeException(nameof(hours));
    if (hours == 0)
      return;
    // Facility sleeper time is a dispatch assumption, never a change to
    // observed ELD duty.
    plannedRestStarted ??= Now;
    Rest(hours);
    shiftLeft -= hours;
    shiftDuty += hours;
    if (hours >= .5)
    {
      breakLeft = 8;
      sinceBreak = 0;
    }
    var continuous = (Now - plannedRestStarted.Value).TotalHours;
    if (country == initialCountry)
      continuous = Math.Max(continuous, timeline?.OngoingRest(Now) ?? 0);
    if (continuous < 10)
      return;
    var restart =
      timeline?.Source.Rule(country)?.RestartHours
      ?? (country == "CA" ? 72 : 34);
    Reset(
      continuous >= restart && cycleMode == HosCycleMode.Restart,
      fullDailyRest: true
    );
  }

  private void CheckScenarioHorizon()
  {
    if (Now > calculationStartedAt.AddDays(90))
      throw new CycleScenarioUnavailableException(
        "ETA unavailable: the 90-day planning horizon was exceeded."
      );
  }
}
