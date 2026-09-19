using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchPapersDateWindowTests
{
  [Fact]
  public void TomorrowPickupAndDeliveryShowTheirOwnDateAfterToday()
  {
    using var context = new BunitContext();
    var today = DateOnly.FromDateTime(DateTime.Today);
    var tomorrow = today.AddDays(1);
    var pickup = Load(100, tomorrow, today.AddDays(4));
    var delivery = Load(200, today.AddDays(-2), tomorrow);
    var current = Load(300, today, today.AddDays(4));
    var later = Load(400, today.AddDays(2), today.AddDays(4));
    var papers = context.Render<DispatchPapers>(parameters =>
      parameters.Add(
        view => view.Trucks,
        [
          new TruckDispatchBoardResponse
          {
            Dispatches = [later, delivery, pickup, current],
          },
        ]
      )
    );
    var column = papers.Find(".dispatch-paper-column--2");
    Assert.Equal(
      "Delivery / pickup today and tomorrow",
      column.QuerySelector("h2")!.TextContent
    );
    var tabs = column.QuerySelectorAll(".dispatch-paper__tab");
    Assert.Equal(3, tabs.Length);
    Assert.Contains("300", tabs[0].TextContent);
    Assert.Contains("100", tabs[1].TextContent);
    Assert.Contains("200", tabs[2].TextContent);
    Assert.StartsWith(
      "Pickup ",
      tabs[1]
        .QuerySelector(".dispatch-paper__tab-schedule")!
        .GetAttribute("aria-label")
    );
    Assert.StartsWith(
      "Delivery ",
      tabs[2]
        .QuerySelector(".dispatch-paper__tab-schedule")!
        .GetAttribute("aria-label")
    );
    Assert.Contains(
      "400",
      papers.Find(".dispatch-paper-column--0").TextContent
    );
  }

  private static DispatchResponse Load(
    int number,
    DateOnly pickup,
    DateOnly delivery
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = number,
      Status = "assigned",
      Stops =
      [
        new()
        {
          Sequence = 1,
          Job = "Pickup",
          ScheduledDate = pickup,
        },
        new()
        {
          Sequence = 2,
          Job = "Delivery",
          ScheduledDate = delivery,
        },
      ],
    };
}
