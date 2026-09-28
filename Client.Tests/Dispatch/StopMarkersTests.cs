using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;

namespace Client.Tests.Dispatch;

// A stop's badge names what it is within its own load: P for a pickup, D
// for the only delivery, D1, D2... for several, never the load's place in a
// chain. Lists and the map say the same.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class StopMarkersTests
{
  [Theory]
  [InlineData(new[] { "Pickup", "Delivery" }, new[] { "P", "D" })]
  [InlineData(
    new[] { "Pickup", "Delivery", "Delivery" },
    new[] { "P", "D1", "D2" }
  )]
  [InlineData(
    new[] { "Pick Up", "pickup", "DROP OFF", "Dropoff" },
    new[] { "P", "P", "D1", "D2" }
  )]
  [InlineData(
    new[] { "Pickup", "Hook trailer", "Delivery" },
    new[] { "P", "2", "D" }
  )]
  public void LabelsFollowTheLoadsOwnStops(string[] jobs, string[] expected)
  {
    Assert.Equal(expected, StopMarkers.Labels(jobs));
  }

  [Fact]
  public void EachLoadCountsItsOwnDeliveries()
  {
    var first = StopMarkers.Labels(["Pickup", "Delivery", "Delivery"]);
    var second = StopMarkers.Labels(["Pickup", "Delivery"]);
    Assert.Equal(["P", "D1", "D2"], first);
    Assert.Equal(["P", "D"], second);
  }

  [Fact]
  public void VisitsCarryTheMarkerInSequenceOrder()
  {
    var visits = DispatchStopPresentation.OrderedVisits(
      [Stop(3, "Delivery"), Stop(1, "Pickup"), Stop(2, "Delivery")]
    );
    Assert.Equal(["P", "D1", "D2"], visits.Select(visit => visit.Marker));
    Assert.Equal([1, 2, 3], visits.Select(visit => visit.Number));
  }

  private static DispatchStopResponse Stop(int sequence, string job) =>
    new()
    {
      Id = Guid.NewGuid(),
      Sequence = sequence,
      Job = job,
    };
}
