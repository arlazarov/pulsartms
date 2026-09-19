using Application.Features.Dispatch.Models;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionBoardIndexTests
{
  [Fact]
  public void SameLoadReturningToTruckKeepsBothExecutionIdentities()
  {
    var load = Guid.NewGuid();
    var truck = Guid.NewGuid();
    var first = Guid.NewGuid();
    var second = Guid.NewGuid();
    var index = new DispatchBoardIndex(
      [
        new()
        {
          Key = truck.ToString(),
          TruckId = truck,
          TruckNumber = "54777",
          Dispatches =
          [
            new()
            {
              Id = load,
              ExecutionLegId = first,
              AssignmentRevision = 3,
              ExecutionStatus = "active",
            },
            new()
            {
              Id = load,
              ExecutionLegId = second,
              AssignmentRevision = 1,
              ExecutionStatus = "planned",
            },
          ],
        },
      ]
    );
    var row = Assert.Single(index.SelectPage(1, 12, null, truck).Items);
    Assert.Collection(
      row.Dispatches,
      current =>
      {
        Assert.Equal(load, current.Id);
        Assert.Equal(first, current.ExecutionLegId);
        Assert.Equal(3, current.AssignmentRevision);
        Assert.Equal("active", current.ExecutionStatus);
      },
      future =>
      {
        Assert.Equal(load, future.Id);
        Assert.Equal(second, future.ExecutionLegId);
        Assert.Equal(1, future.AssignmentRevision);
        Assert.Equal("planned", future.ExecutionStatus);
      }
    );
  }
}
