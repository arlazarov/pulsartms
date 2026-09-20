using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Fleet.Models;

namespace Application.Features.Eta.Algorithms;

// Forecast using standard property-carrier rules, not an ELD compliance
// verdict.
// History credits require a continuous verified timeline; missing data keeps
// conservative rests.
public sealed class HosTravelClock
{
  public DateTimeOffset Now { get; private set; }
  public double RestHours { get; private set; }
  public double DriveHours { get; private set; }
  private double driveLeft,
    shiftLeft,
    cycleLeft,
    breakLeft;
  private double shiftDrive,
    shiftDuty,
    sinceBreak;
  private DateTimeOffset shiftStart;
  private string country;
  private readonly string initialCountry;
  private readonly HosTimeline? timeline;
  public bool HistoryAvailable => timeline is not null;
  public bool RecapVerified => CycleFeasibility.RecapVerified;
  public int SplitRests { get; private set; }
  public int RecapWaits { get; private set; }
  public bool CompletedOngoingRest { get; private set; }
  public double PreTripHours { get; private set; }
  public double FuelHours { get; private set; }
  public double PlannedOffDutyWaitHours { get; private set; }
  private DateTimeOffset? plannedRestStarted;
  private readonly EtaPlanningOptions? planning;
  private readonly HosCycleMode cycleMode;
  private readonly DateTimeOffset calculationStartedAt;
  public HosCycleFeasibility CycleFeasibility { get; }
  public DateTimeOffset? CycleRestStartedAt { get; private set; }
  public DateTimeOffset? CycleResumeAt { get; private set; }
  private double plannedDriveLeft;
  private double plannedBreakLeft;
  private bool preTripPending;
  private bool plannedBreakTaken;
  private bool plannedFuelTaken;

  public HosTravelClock(
    DateTimeOffset now,
    DriverHosClocks clocks,
    string initialCountry,
    HosHistory? history = null,
    EtaPlanningOptions? planning = null,
    HosCycleMode cycleMode = HosCycleMode.Observe
  )
  {
    this.planning = planning;
    this.cycleMode = cycleMode;
    calculationStartedAt = now;
    CycleFeasibility = new(now, clocks, initialCountry, history);
    Now = now;
    country = initialCountry;
    this.initialCountry = initialCountry;
    timeline = HosTimeline.Create(history, now);
    driveLeft = Math.Max(0, clocks.DriveMs!.Value / 3600000d);
    shiftLeft = Math.Max(0, clocks.ShiftMs!.Value / 3600000d);
    cycleLeft = Math.Max(0, clocks.CycleMs!.Value / 3600000d);
    breakLeft = Math.Max(0, (clocks.BreakMs ?? 0) / 3600000d);
    shiftDrive = Math.Max(0, (country == "CA" ? 13 : 11) - driveLeft);
    shiftDuty = Math.Max(shiftDrive, 14 - shiftLeft);
    sinceBreak = Math.Max(0, 8 - breakLeft);
    shiftStart = now.AddHours(-shiftDuty);
    plannedDriveLeft = Math.Max(
      0,
      (planning?.DrivingHoursPerShift ?? double.MaxValue) - shiftDrive
    );
    plannedBreakLeft = 8;
    preTripPending = planning is not null && shiftDrive < .000001;
    plannedFuelTaken = planning is not null && !preTripPending;
  }

  public void Enter(string nextCountry)
  {
    if (country == nextCountry)
      return;
    country = nextCountry;
    CycleFeasibility.Enter(nextCountry);
    // Retain consumption across the border; never award new hours there.
    driveLeft = Math.Min(
      driveLeft,
      Math.Max(0, (country == "CA" ? 13 : 11) - shiftDrive)
    );
    shiftLeft = Math.Min(shiftLeft, Math.Max(0, 14 - shiftDuty));
    breakLeft = Math.Min(breakLeft, Math.Max(0, 8 - sinceBreak));
  }

  public void CompleteOngoingDailyRest(DriverDutyStatus? status)
  {
    if (
      country != "US"
      || status?.RestStartedAt is not { } start
      || status.ObservedAt < Now.AddMinutes(-3)
      || (status.ObservedAt - start).TotalHours < 3
    )
      return;
    var remaining = Math.Max(0, 10 - (Now - start).TotalHours);
    // A rest the ELD has not credited is not a shift this may invent. Where
    // the driver has stood ten hours and the clocks still read a shift in
    // progress, the feed is behind the yard, and resetting here would award
    // hours nobody granted - and with them a pre-trip and a fuel allowance
    // the driver already spent this morning. Waiting out a rest that has not
    // finished is a different thing, and still allowed: those hours are
    // spent in the forecast before they are used.
    if (remaining <= 0 && (shiftDrive > .000001 || shiftDuty > .000001))
      return;
    Rest(remaining);
    driveLeft = 11;
    shiftLeft = 14;
    breakLeft = 8;
    shiftDrive = shiftDuty = sinceBreak = 0;
    shiftStart = Now;
    RefreshCycle();
    CompletedOngoingRest = true;
    StartPlannedShift();
  }

  private void StartPlannedShift()
  {
    plannedDriveLeft = planning?.DrivingHoursPerShift ?? double.MaxValue;
    plannedBreakLeft = 8;
    plannedBreakTaken = false;
    plannedFuelTaken = false;
    preTripPending = planning is not null;
  }

  public void Fuel()
  {
    if (planning is not null && plannedFuelTaken)
      return;
    plannedFuelTaken = true;
    var hours = (planning?.FuelStopMinutes ?? 15) / 60d;
    FuelHours += hours;
    Service(hours);
  }

  public void CompletePlannedBreak()
  {
    if (planning is null || plannedBreakTaken)
      return;
    var hours = planning.DailyBreakMinutes / 60d;
    Rest(hours);
    shiftLeft -= hours;
    shiftDuty += hours;
    breakLeft = 8;
    sinceBreak = 0;
    plannedBreakTaken = true;
  }

  private void Rest(double hours, string status = "sleeperBerth")
  {
    var start = Now;
    Now = Now.AddTicks((long)Math.Round(hours * TimeSpan.TicksPerHour));
    RestHours += hours;
    timeline?.Add(start, Now, status);
    CycleFeasibility.Observe(start, Now, status);
    CheckScenarioHorizon();
  }

  private void RefreshCycle()
  {
    if (CycleFeasibility.BalanceHours(Now) is { } available)
      cycleLeft = available;
  }

  public StopCycleForecast SnapshotCycle()
  {
    return CycleFeasibility.Snapshot(Now)
      ?? new(CycleMinutes(cycleLeft), null, null, null, false);
  }

  private static int CycleMinutes(double hours) =>
    (int)Math.Min(int.MaxValue, Math.Floor(Math.Max(0, hours) * 60));

  private bool WaitForScenarioRecap()
  {
    if (CycleFeasibility.NextUsableRecap(Now) is not { } boundary)
      return false;
    var start = Now;
    var rest = (boundary - Now).TotalHours;
    Rest(rest, "offDuty");
    CycleRestStartedAt ??= start;
    CycleResumeAt ??= Now;
    shiftLeft -= rest;
    shiftDuty += rest;
    breakLeft = 8;
    sinceBreak = 0;
    if ((timeline?.OngoingRest(Now) ?? 0) >= 10)
      Reset(false, fullDailyRest: true);
    RefreshCycle();
    RecapWaits++;
    return true;
  }

  private void Reset(bool cycle, bool fullDailyRest = false)
  {
    if (cycleMode is HosCycleMode.Observe or HosCycleMode.Recap)
      cycle = false;
    if (
      !cycle
      && !fullDailyRest
      && country == initialCountry
      && timeline?.FindSplit(Now, country) is { } split
    )
    {
      Rest(split.RestHours);
      driveLeft = split.DriveLeft;
      shiftLeft = split.ShiftLeft;
      if (country == "CA")
      {
        driveLeft = Math.Min(
          driveLeft,
          Math.Max(
            0,
            13 - timeline.Hours(Now.AddHours(-24), Now, p => p.Driving)
          )
        );
        shiftLeft = Math.Min(
          shiftLeft,
          Math.Max(0, 14 - timeline.Hours(Now.AddHours(-24), Now, p => p.Duty))
        );
      }
      if (driveLeft > .00001 && shiftLeft > .00001)
      {
        shiftDrive = (country == "CA" ? 13 : 11) - driveLeft;
        shiftDuty = 14 - shiftLeft;
        shiftStart = Now.AddHours(-Math.Max(0, shiftDuty));
        breakLeft = 8;
        sinceBreak = 0;
        RefreshCycle();
        SplitRests++;
        return;
      }
    }
    // Canada: 72h is a conservative cycle restart for either cycle.
    double hours = cycle ? (country == "CA" ? 72 : 34) : 10;
    var rule = timeline?.Source.Rule(country);
    if (cycle && rule is not null)
      hours = rule.RestartHours;
    var ongoing =
      country == initialCountry ? timeline?.OngoingRest(Now) ?? 0 : 0;
    if (plannedRestStarted is { } plannedStart)
      ongoing = Math.Max(ongoing, (Now - plannedStart).TotalHours);
    var restStartedAt = Now.AddHours(-ongoing);
    hours = Math.Max(0, hours - ongoing);
    if (country == "CA")
      hours = Math.Max(hours, (shiftStart.AddHours(24) - Now).TotalHours);
    Rest(hours);
    if (cycle && cycleMode == HosCycleMode.Restart)
    {
      CycleFeasibility.CreditRestart(Now);
      CycleRestStartedAt ??= restStartedAt;
      CycleResumeAt ??= Now;
    }
    driveLeft = country == "CA" ? 13 : 11;
    shiftLeft = 14;
    if (cycle)
      cycleLeft = rule?.Hours ?? (country == "CA" ? 70 : 60);
    RefreshCycle();
    breakLeft = 8;
    shiftDrive = shiftDuty = sinceBreak = 0;
    shiftStart = Now;
    StartPlannedShift();
  }

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
      if (planning is not null && !plannedFuelTaken)
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
