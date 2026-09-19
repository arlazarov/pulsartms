using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchPaperGroupingTests
{
  [Theory]
  [InlineData(-1, 0)]
  [InlineData(0, 2)]
  [InlineData(1, 2)]
  [InlineData(2, 0)]
  public void PickupAndDeliveryUseTheTwoCalendarDayWindow(
    int offset,
    int column
  )
  {
    var today = new DateOnly(2026, 9, 13);
    var date = today.AddDays(offset);
    foreach (var pickup in new[] { true, false })
    {
      var load = new DispatchResponse
      {
        Status = "assigned",
        ShipDate = pickup ? date : today.AddDays(-3),
        DeliveryDate = pickup ? today.AddDays(4) : date,
      };
      Assert.Equal(column, new DispatchBoardRow(new(), load).Column(today));
    }
  }

  [Theory]
  [InlineData(2026, 9, 30)]
  [InlineData(2026, 12, 31)]
  [InlineData(2028, 2, 28)]
  public void TomorrowWorksAcrossCalendarBoundaries(
    int year,
    int month,
    int day
  )
  {
    var today = new DateOnly(year, month, day);
    Assert.True(DispatchBoardRow.IsTodayOrTomorrow(today.AddDays(1), today));
    Assert.False(DispatchBoardRow.IsTodayOrTomorrow(today.AddDays(2), today));
    Assert.False(DispatchBoardRow.IsTodayOrTomorrow(null, today));
  }

  [Theory]
  [InlineData("planned", 0)]
  [InlineData("unassigned", 0)]
  [InlineData("in_transit", 1)]
  public void TomorrowPreservesExistingStatusPriority(string status, int column)
  {
    var today = new DateOnly(2026, 9, 13);
    var load = new DispatchResponse
    {
      Status = status,
      ShipDate = today.AddDays(1),
      DeliveryDate = today.AddDays(1),
    };
    Assert.Equal(column, new DispatchBoardRow(new(), load).Column(today));
  }

  [Theory]
  [InlineData("assigned", false, 2)]
  [InlineData("assigned", true, 1)]
  [InlineData("in_transit", false, 1)]
  public void PickupTodayMovesToTransitAfterPickup(
    string status,
    bool pickedUp,
    int column
  )
  {
    var today = new DateOnly(2026, 9, 10);
    var load = new DispatchResponse
    {
      Status = status,
      Stops =
      [
        new()
        {
          Sequence = 1,
          Job = "Pick Up",
          ScheduledDate = today,
          PickedUpAt = pickedUp
            ? new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc)
            : null,
        },
        new()
        {
          Sequence = 2,
          Job = "Drop Off",
          ScheduledDate = today.AddDays(1),
        },
      ],
    };
    Assert.Equal(column, new DispatchBoardRow(new(), load).Column(today));
  }
}
