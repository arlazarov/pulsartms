using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchBoardScheduleTests
{
    [Fact]
    public void SameDayWindowIncludesItsEndTimeWithoutRepeatingTheDate()
    {
        var stop = Window();
        Assert.Equal("Sep 9 · 07:00 AM – 02:00 PM", DispatchBoardRow.Schedule(stop));
        stop.ScheduledDate2 = null;
        Assert.Equal("Sep 9 · 07:00 AM – 02:00 PM", DispatchBoardRow.Schedule(stop));
    }

    [Fact]
    public void CrossDayWindowKeepsTheSuppliedEndDate()
    {
        var stop = Window();
        stop.ScheduledDate2 = new(2026, 9, 10);
        Assert.Equal("Sep 9 · 07:00 AM – Sep 10 · 02:00 PM", DispatchBoardRow.Schedule(stop));
        stop.ScheduledTime2 = null;
        Assert.Equal("Sep 9 · 07:00 AM – Sep 10", DispatchBoardRow.Schedule(stop));
    }

    [Fact]
    public void AppointmentIgnoresStaleEndFieldsUnlessItIsAWindow()
    {
        var stop = Window();
        stop.IsWindow = false;
        Assert.Equal("Sep 9 · 07:00 AM", DispatchBoardRow.Schedule(stop));
        stop.IsWindow = true;
        stop.ScheduledDate2 = null;
        stop.ScheduledTime2 = null;
        Assert.Equal("Sep 9 · 07:00 AM", DispatchBoardRow.Schedule(stop));
    }

    [Fact]
    public void MissingDatesStayUnknownUnlessTheCallerSuppliesAFallback()
    {
        var stop = Window();
        stop.ScheduledDate = null;
        stop.ScheduledDate2 = null;
        Assert.Equal("07:00 AM – 02:00 PM", DispatchBoardRow.Schedule(stop));
        Assert.Equal("Sep 11 · 07:00 AM – 02:00 PM", DispatchBoardRow.Schedule(stop, new(2026, 9, 11)));
        stop.ScheduledDate = new(2026, 9, 9);
        Assert.Equal("Sep 9 · 07:00 AM – 02:00 PM", DispatchBoardRow.Schedule(stop, new(2026, 9, 11)));
        Assert.Equal("Date pending", DispatchBoardRow.Schedule(null));
        Assert.Equal("Sep 11", DispatchBoardRow.Schedule(null, new(2026, 9, 11)));
    }

    private static DispatchStopResponse Window() => new()
    {
        IsWindow = true, ScheduledDate = new(2026, 9, 9), ScheduledTime = new(7, 0),
        ScheduledDate2 = new(2026, 9, 9), ScheduledTime2 = new(14, 0)
    };
}
