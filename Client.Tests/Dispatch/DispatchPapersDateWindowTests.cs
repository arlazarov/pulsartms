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

  // The nearest event not yet done orders the folders: a pickup already
  // done never does, and a truck's started load stands before its next one
  // (the owner, September 27 - 11006's delivery before its next pickup).
  [Fact]
  public void WorkIsOrderedByTheNextUnfinishedEvent()
  {
    using var context = new BunitContext();
    var today = DateOnly.FromDateTime(DateTime.Today);
    var tomorrow = today.AddDays(1);
    // Picked up this morning, delivering tomorrow at nine; still "assigned"
    // at the source, so it reads in the today-and-tomorrow folder.
    var current = Load(1395, today, tomorrow);
    current.Stops[0].ScheduledTime = new(6, 0);
    current.Stops[0].IsCompleted = true;
    current.Stops[1].ScheduledTime = new(9, 0);
    // The same truck's next load, due to be picked up this evening.
    var next = Load(1412, today, tomorrow.AddDays(1));
    next.Stops[0].ScheduledTime = new(20, 0);
    // Another truck's pickup at noon.
    var other = Load(1500, today, tomorrow.AddDays(2));
    other.Stops[0].ScheduledTime = new(12, 0);
    var papers = context.Render<DispatchPapers>(parameters =>
      parameters.Add(
        view => view.Trucks,
        [
          new TruckDispatchBoardResponse
          {
            Key = "11006",
            TruckNumber = "11006",
            Dispatches = [current, next],
          },
          new TruckDispatchBoardResponse
          {
            Key = "54777",
            TruckNumber = "54777",
            Dispatches = [other],
          },
        ]
      )
    );

    var tabs = papers
      .Find(".dispatch-paper-column--2")
      .QuerySelectorAll(".dispatch-paper__tab");
    Assert.Equal(
      ["1500", "1395", "1412"],
      tabs.Select(tab =>
          tab.QuerySelector(".dispatch-paper__tab-number")!.TextContent.Trim()
        )
        .ToArray()
    );
    Assert.StartsWith(
      "Delivery ",
      tabs[1]
        .QuerySelector(".dispatch-paper__tab-schedule")!
        .GetAttribute("aria-label")
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
