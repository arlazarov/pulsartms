using Client.Models.DTO.Dispatch.Workspace;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchStopDraftOrderTests
{
  [Fact]
  public void FutureStopsMoveWithoutChangingTheirIdentityOrAssignment()
  {
    var stops = new List<DispatchWorkspaceStop>
    {
      Stop("first"),
      Stop("second"),
      Stop("third"),
    };
    var third = stops[2];
    third.StopNo = "DEL-300";
    third.TruckNumber = "54777";

    Assert.True(DispatchStopDraftOrder.Move(stops, third.Id, 0));

    Assert.Same(third, stops[0]);
    Assert.Equal("DEL-300", stops[0].StopNo);
    Assert.Equal("54777", stops[0].TruckNumber);
    Assert.Equal([1, 2, 3], stops.Select(stop => stop.Sequence));
  }

  [Theory]
  [InlineData(false, "segment")]
  [InlineData(true, "different-segment")]
  public void CannotMoveAcrossRecordedStopOrExecutionBoundary(
    bool movable,
    string segment
  )
  {
    var stops = new List<DispatchWorkspaceStop>
    {
      Stop("first"),
      Stop("boundary"),
      Stop("third"),
    };
    stops[1].CanMove = movable;
    stops[1].SegmentKey = segment;
    var ids = stops.Select(stop => stop.Id).ToArray();

    Assert.False(DispatchStopDraftOrder.Move(stops, stops[2].Id, 0));
    Assert.Equal(ids, stops.Select(stop => stop.Id));
  }

  [Fact]
  public void InvalidMoveTargetsDoNotMutateDraft()
  {
    var stop = Stop("first");
    var stops = new List<DispatchWorkspaceStop> { stop };

    foreach (var target in new[] { -1, 0, 1 })
      Assert.False(DispatchStopDraftOrder.Move(stops, stop.Id, target));
    Assert.False(DispatchStopDraftOrder.Move(stops, Guid.NewGuid(), 0));
    Assert.Same(stop, Assert.Single(stops));
  }

  private static DispatchWorkspaceStop Stop(string name) =>
    new()
    {
      Id = Guid.NewGuid(),
      Name = name,
      CanEdit = true,
      CanMove = true,
      SegmentKey = "segment",
    };
}
