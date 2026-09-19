using Client.Models.DTO.Planning;
using Client.Pages.FleetMap;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class FleetAppointmentDisplayTests
{
  private static PlanStop Stop =>
    new(Guid.NewGuid(), "Plant", "2120 NC-71", 1, new(40, -80))
    {
      ScheduledDate = new(2026, 9, 15),
      ScheduledTime = new(9, 0),
      ScheduledTime2 = new(20, 0),
    };

  [Fact]
  public void SameDayDateIsSeparateAndClockSuffixesCannotBeOrphaned()
  {
    var result = FleetAppointmentDisplay.Split(Stop);
    Assert.Equal("Sep 15", result.Date);
    Assert.Equal("09:00\u00a0AM – 08:00\u00a0PM", result.Value);
  }

  [Fact]
  public void MultiDayWindowRetainsBothDates()
  {
    var result = FleetAppointmentDisplay.Split(
      Stop with
      {
        ScheduledDate2 = new(2026, 9, 16),
      }
    );
    Assert.Null(result.Date);
    Assert.Equal(
      "Sep 15 · 09:00\u00a0AM – Sep 16 · 08:00\u00a0PM",
      result.Value
    );
  }

  [Fact]
  public void MissingAndDateOnlyAppointmentsRetainTheirFacts()
  {
    Assert.Equal((null, "—"), FleetAppointmentDisplay.Split(null));
    Assert.Equal(
      (null, "Sep 15"),
      FleetAppointmentDisplay.Split(
        Stop with
        {
          ScheduledTime = null,
          ScheduledTime2 = null,
        }
      )
    );
  }
}
