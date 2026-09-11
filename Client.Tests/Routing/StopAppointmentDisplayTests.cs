using System.Globalization;
using Client.Models.DTO.Planning;
using Client.Services;

namespace Client.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class StopAppointmentDisplayTests
{
  private static PlanStop Stop => new(Guid.NewGuid(), "Warehouse", "123 Main Street", 1, new(40, -80));

  [Theory]
  [InlineData(0, "12:00 AM")]
  [InlineData(12, "12:00 PM")]
  [InlineData(14, "02:00 PM")]
  [InlineData(23, "11:00 PM")]
  public void ClockUsesTwelveHourTimeWithoutChangingTheLocalHour(int hour, string expected) =>
    Assert.Equal(expected, StopAppointmentDisplay.Format(Stop with { ScheduledTime = new(hour, 0) }));

  [Fact]
  public void SameDayWindowShowsItsDateOnceAndCollapsesIdenticalDisplayTimes()
  {
    var stop = Stop with
    {
      ScheduledDate = new(2026, 9, 9), ScheduledTime = new(7, 0),
      ScheduledDate2 = new(2026, 9, 9), ScheduledTime2 = new(14, 0)
    };
    Assert.Equal("Sep 9 · 07:00 AM – 02:00 PM", StopAppointmentDisplay.Format(stop));
    Assert.Equal("Sep 9 · 07:00 AM – 02:00 PM", StopAppointmentDisplay.Format(stop with { ScheduledDate2 = null }));
    Assert.Equal("Sep 9 · 07:00 AM", StopAppointmentDisplay.Format(stop with { ScheduledTime2 = new(7, 0, 59) }));
  }

  [Fact]
  public void CrossDayAndCrossYearWindowsKeepTheirCalendarBoundaries()
  {
    Assert.Equal("Sep 9 · 12:00 AM – Sep 10 · 05:15 AM", StopAppointmentDisplay.Format(Stop with
    {
      ScheduledDate = new(2026, 9, 9), ScheduledTime = new(0, 0),
      ScheduledDate2 = new(2026, 9, 10), ScheduledTime2 = new(5, 15, 59)
    }));
    Assert.Equal("Dec 31, 2026 · 11:00 PM – Jan 1, 2027 · 01:00 AM", StopAppointmentDisplay.Format(Stop with
    {
      ScheduledDate = new(2026, 12, 31), ScheduledTime = new(23, 0),
      ScheduledDate2 = new(2027, 1, 1), ScheduledTime2 = new(1, 0)
    }));
  }

  [Fact]
  public void UnknownPartsRemainUnknownAndMidnightIsNotTreatedAsAnUnsetTime()
  {
    Assert.Equal("—", StopAppointmentDisplay.Format(null));
    Assert.Equal("—", StopAppointmentDisplay.Format(Stop));
    Assert.Equal("Sep 9", StopAppointmentDisplay.Format(Stop with { ScheduledDate = new(2026, 9, 9) }));
    Assert.Equal("07:00 AM", StopAppointmentDisplay.Format(Stop with { ScheduledTime = new(7, 0) }));
    Assert.Equal("Jan 1 · 12:00 AM", StopAppointmentDisplay.Format(Stop with
    {
      ScheduledDate = new(2026, 1, 1), ScheduledTime = new(0, 0, 59)
    }));
  }

  [Theory]
  [InlineData("fr-FR")]
  [InlineData("ar-SA")]
  [InlineData("tr-TR")]
  public void FormattingUsesTheSuppliedLocalCalendarAndStableEnglishLabels(string culture)
  {
    var previous = CultureInfo.CurrentCulture;
    var previousUi = CultureInfo.CurrentUICulture;
    try
    {
      CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
      CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
      Assert.Equal("Sep 9 · 07:00 AM – 02:00 PM", StopAppointmentDisplay.Format(Stop with
      {
        ScheduledDate = new(2026, 9, 9), ScheduledTime = new(7, 0),
        ScheduledDate2 = new(2026, 9, 9), ScheduledTime2 = new(14, 0)
      }));
    }
    finally
    {
      CultureInfo.CurrentCulture = previous;
      CultureInfo.CurrentUICulture = previousUi;
    }
  }
}
