namespace Application.Features.Eta.Models;

public sealed record HosPeriod(
  DateTimeOffset Start,
  DateTimeOffset End,
  string Status
)
{
  public bool Driving => Status == "driving";
  public bool Duty => Status is "driving" or "onDuty" or "yardMove";
  public bool Rest =>
    Status is "offDuty" or "sleeperBerth" or "personalConveyance";
}

public sealed record HosCycleRule(int Days, double Hours, double RestartHours);

public sealed record HosHistory(
  DateTimeOffset From,
  DateTimeOffset Through,
  string TimeZoneId,
  int DayStartHour,
  HosCycleRule? UsCycle,
  HosCycleRule? CanadaCycle,
  IReadOnlyList<HosPeriod> Periods
)
{
  public HosCycleRule? Rule(string country) =>
    country == "CA" ? CanadaCycle : UsCycle;
}
