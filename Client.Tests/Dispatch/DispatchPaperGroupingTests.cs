using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchPaperGroupingTests
{
    [Theory]
    [InlineData("assigned", false, 2)]
    [InlineData("assigned", true, 1)]
    [InlineData("in_transit", false, 1)]
    public void PickupTodayMovesToTransitAfterPickup(string status, bool pickedUp, int column)
    {
        var today = new DateOnly(2026, 9, 10);
        var load = new DispatchResponse { Status = status, Stops = [
            new() { Sequence = 1, Job = "Pick Up", ScheduledDate = today,
                PickedUpAt = pickedUp ? new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc) : null },
            new() { Sequence = 2, Job = "Drop Off", ScheduledDate = today.AddDays(1) }] };
        Assert.Equal(column, new DispatchBoardRow(new(), load).Column(today));
    }
}
