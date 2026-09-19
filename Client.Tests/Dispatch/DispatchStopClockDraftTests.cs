using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchStopClockDraftTests
{
  [Fact]
  public void WindowKeepsBothDatesAndUsesTwelveHourTimes()
  {
    var stop = new DispatchWorkspaceStop
    {
      AppointmentMode = "window",
      ScheduledDate = new(2026, 9, 14),
      ScheduledDate2 = new(2026, 9, 15),
      ScheduledTime = new(23, 30),
      ScheduledTime2 = new(7, 15),
    };
    var clock = DispatchStopClockDraft.From(stop);

    Assert.Equal("11:30 PM", clock.Start);
    Assert.Equal("07:15 AM", clock.End);
    clock.End = "08:45 AM";
    Assert.True(clock.Apply(stop, out var error));
    Assert.Null(error);
    Assert.Equal(new TimeOnly(8, 45), stop.ScheduledTime2);
    Assert.Equal(new DateOnly(2026, 9, 15), stop.ScheduledDate2);
  }

  [Fact]
  public void InvalidTextDoesNotReplaceConfirmedClockValues()
  {
    var stop = new DispatchWorkspaceStop
    {
      AppointmentMode = "window",
      ScheduledTime = new(7, 0),
      ScheduledTime2 = new(9, 0),
    };
    var clock = DispatchStopClockDraft.From(stop);
    clock.Start = "10:00 AM";
    clock.End = "tomorrow";

    Assert.False(clock.Apply(stop, out var error));
    Assert.NotNull(error);
    Assert.Equal(new TimeOnly(7, 0), stop.ScheduledTime);
    Assert.Equal(new TimeOnly(9, 0), stop.ScheduledTime2);
  }

  [Fact]
  public void ExactAppointmentAllowsAnUnknownTime()
  {
    var stop = new DispatchWorkspaceStop
    {
      AppointmentMode = "at",
      ScheduledDate = new(2026, 9, 14),
    };

    Assert.True(DispatchStopClockDraft.From(stop).Apply(stop, out _));
    Assert.Null(stop.ScheduledTime);
    Assert.Equal(new DateOnly(2026, 9, 14), stop.ScheduledDate);
  }

  [Fact]
  public void UnchangedPreciseClockIsNotRoundedByEditingOtherFields()
  {
    var stop = new DispatchWorkspaceStop
    {
      AppointmentMode = "at",
      ScheduledTime = new TimeOnly(7, 0, 17, 123),
    };
    var before = stop.ScheduledTime;

    Assert.True(DispatchStopClockDraft.From(stop).Apply(stop, out _));
    Assert.Equal(before, stop.ScheduledTime);
  }
}
