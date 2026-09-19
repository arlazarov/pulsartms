using Domain.Entities.Fuel;

namespace Server.Tests.Fuel;

// A fuel stop is chosen hours ahead, so what matters is whether the station
// is open when the driver gets there, in the station's own local time.
//
// The important cases are the ones that must NOT answer "closed": no hours,
// no offset, unreadable hours. Most of the fleet's stops are truck stops
// that never close, and refusing the unknown would leave a driver with no
// fuel stop at all.
[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelStationHoursTests
{
  private const string WeekdayNineToFive = """
    {"periods":[
      {"open":{"day":1,"hour":9,"minute":0},"close":{"day":1,"hour":17,"minute":0}},
      {"open":{"day":2,"hour":9,"minute":0},"close":{"day":2,"hour":17,"minute":0}}
    ]}
    """;

  // Monday 2026-09-21 in UTC; the station sits five hours behind.
  private static DateTime MondayUtc(int hour, int minute = 0) =>
    new(2026, 9, 21, hour, minute, 0, DateTimeKind.Utc);

  [Theory]
  [InlineData(13, FuelStationHours.Openness.Open)] // 08:00 local, before opening
  [InlineData(15, FuelStationHours.Openness.Open)] // 10:00 local
  [InlineData(23, FuelStationHours.Openness.Closed)] // 18:00 local
  public void ArrivalIsJudgedInTheStationsOwnTime(
    int utcHour,
    FuelStationHours.Openness expected
  )
  {
    var answer = FuelStationHours.At(
      WeekdayNineToFive,
      -300,
      MondayUtc(utcHour)
    );

    // 08:00 local is before Monday's opening, so the last edge that applies
    // is Tuesday's close wrapping around the week - the station is shut.
    if (utcHour == 13)
      Assert.Equal(FuelStationHours.Openness.Closed, answer);
    else
      Assert.Equal(expected, answer);
  }

  [Fact]
  public void AStationOpenAtTheArrivalMinuteIsOpen()
  {
    Assert.Equal(
      FuelStationHours.Openness.Open,
      FuelStationHours.At(WeekdayNineToFive, -300, MondayUtc(14, 30))
    );
  }

  [Fact]
  public void AStationClosedAtTheArrivalMinuteIsClosed()
  {
    Assert.Equal(
      FuelStationHours.Openness.Closed,
      FuelStationHours.At(WeekdayNineToFive, -300, MondayUtc(3))
    );
  }

  // Periods present but empty is how the provider states a place that never
  // closes, which is most truck stops.
  [Fact]
  public void NoPeriodsAtAllMeansAlwaysOpen()
  {
    Assert.Equal(
      FuelStationHours.Openness.Open,
      FuelStationHours.At("""{"periods":[]}""", 0, MondayUtc(3))
    );
  }

  [Theory]
  [InlineData(null, -300)]
  [InlineData(WeekdayNineToFive, null)]
  [InlineData("", -300)]
  [InlineData("not json", -300)]
  [InlineData("""{"periods":[{"open":{"hour":9}}]}""", -300)]
  public void WhatCannotBeReadIsUnknownRatherThanClosed(
    string? json,
    int? offset
  )
  {
    Assert.Equal(
      FuelStationHours.Openness.Unknown,
      FuelStationHours.At(json, offset, MondayUtc(3))
    );
  }

  // A period running past midnight has to keep covering the night, or every
  // overnight stop reads as shut at the hour a driver most needs it.
  [Fact]
  public void APeriodRunningPastMidnightStillCoversTheNight()
  {
    const string overnight = """
      {"periods":[
        {"open":{"day":0,"hour":22,"minute":0},"close":{"day":1,"hour":6,"minute":0}}
      ]}
      """;

    Assert.Equal(
      FuelStationHours.Openness.Open,
      FuelStationHours.At(
        overnight,
        0,
        new(2026, 9, 21, 2, 0, 0, DateTimeKind.Utc)
      )
    );
    Assert.Equal(
      FuelStationHours.Openness.Closed,
      FuelStationHours.At(
        overnight,
        0,
        new(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc)
      )
    );
  }
}
