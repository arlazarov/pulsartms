using Bunit;
using Client.Models.DTO.Dispatch;
using Client.Pages.Dispatch;
using Client.Shared.Dispatch;

namespace Client.Tests.Dispatch;

// A load being handed from one truck to another stands under both of them,
// and a truck driving two legs of a load stands against it twice. Both the
// table and the papers flatten every truck's loads into one list, so both
// had two siblings carrying the same key - and Blazor answers a duplicate
// key by throwing, on every render, where the dispatcher can see it.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Component")]
public sealed class DispatchBoardRowKeyTests
{
  [Fact]
  public void OneLoadOnTwoTrucksIsTwoRowsWithKeysOfTheirOwn()
  {
    var load = Load();
    var outgoing = Truck("outgoing", load);
    var incoming = Truck("incoming", load);
    Assert.NotEqual(
      new DispatchBoardRow(outgoing, outgoing.Dispatches[0]).Key,
      new DispatchBoardRow(incoming, incoming.Dispatches[0]).Key
    );
  }

  [Fact]
  public void OneTruckDrivingTwoLegsOfALoadIsTwoRowsWithKeysOfTheirOwn()
  {
    var truck = Truck("truck", Load(), Load());
    truck.Dispatches[1].Id = truck.Dispatches[0].Id;
    truck.Dispatches[0].ExecutionLegId = Guid.NewGuid();
    truck.Dispatches[1].ExecutionLegId = Guid.NewGuid();
    Assert.NotEqual(
      new DispatchBoardRow(truck, truck.Dispatches[0]).Key,
      new DispatchBoardRow(truck, truck.Dispatches[1]).Key
    );
  }

  [Fact]
  public void PapersDrawOneLoadUnderBothItsTrucksAndRedrawThem()
  {
    using var context = new BunitContext();
    var load = Load();
    var papers = context.Render<DispatchPapers>(parameters =>
      parameters.Add(
        view => view.Trucks,
        [Truck("outgoing", load), Truck("incoming", load)]
      )
    );
    var entries = papers.FindAll(".dispatch-paper-tab-entry");
    Assert.Equal(2, entries.Count);
    // The render that threw was the second one: the first builds the tree,
    // and the duplicate is found when the next one is diffed against it.
    papers.Render();
    Assert.Equal(2, papers.FindAll(".dispatch-paper-tab-entry").Count);
  }

  [Fact]
  public void TableDrawsOneLoadUnderBothItsTrucksAndRedrawsThem()
  {
    using var context = new BunitContext();
    var load = Load();
    var table = context.Render<DispatchTable>(parameters =>
      parameters.Add(
        view => view.Trucks,
        [Truck("outgoing", load), Truck("incoming", load)]
      )
    );
    Assert.Equal(2, table.FindAll(".dispatch-table__row").Count);
    table.Render();
    Assert.Equal(2, table.FindAll(".dispatch-table__row").Count);
  }

  private static TruckDispatchBoardResponse Truck(
    string key,
    params DispatchResponse[] loads
  ) =>
    new()
    {
      Key = key,
      TruckId = Guid.NewGuid(),
      TruckNumber = key,
      // Each truck holds its own copy, as the board's own reader hands
      // them over: the same load, said twice.
      Dispatches = loads.Select(Copy).ToList(),
    };

  private static DispatchResponse Copy(DispatchResponse load) =>
    new()
    {
      Id = load.Id,
      ExecutionLegId = load.ExecutionLegId,
      LoadNumber = load.LoadNumber,
      Status = load.Status,
      Stops = load.Stops,
    };

  private static DispatchResponse Load()
  {
    var today = DateOnly.FromDateTime(DateTime.Today);
    return new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = 4100,
      Status = "assigned",
      Stops =
      [
        new()
        {
          Sequence = 1,
          Job = "Pickup",
          ScheduledDate = today,
        },
        new()
        {
          Sequence = 2,
          Job = "Delivery",
          ScheduledDate = today,
        },
      ],
    };
  }
}
