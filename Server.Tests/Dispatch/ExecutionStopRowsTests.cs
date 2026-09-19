using System.Text.Json;
using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionStopRowsTests
{
  [Fact]
  public void ReplacementKeepsOccurrenceIdentityAndIndependentArrayOrder()
  {
    var first = Stop(7);
    var second = Stop(7);
    var removed = Stop(8);
    var added = Stop(9);
    var leg = new ExecutionLeg { Id = Guid.NewGuid() };
    ExecutionStopRows.Replace(leg, [first, second, removed]);
    var retained = leg.Stops.Single(x => x.Id == first.Id);
    first.Notes = "Corrected";

    ExecutionStopRows.Replace(leg, [second, first, added]);

    Assert.Same(retained, leg.Stops.Single(x => x.Id == first.Id));
    Assert.Equal("Corrected", retained.Notes);
    Assert.Equal(
      new[] { second.Id, first.Id, added.Id },
      ExecutionStopRows.Read(leg).Select(x => x.Id)
    );
    Assert.All(leg.Stops, x => Assert.Equal(leg.Id, x.ExecutionLegId));
    Assert.DoesNotContain(leg.Stops, x => x.Id == removed.Id);
    Assert.Equal(
      new[] { 1, 2, 3 },
      ExecutionStopRows.Read(leg).Select(x => x.Sequence)
    );
    Assert.Equal(7, first.Sequence);
    Assert.Equal(7, second.Sequence);
    first.Notes = "Detached change";
    Assert.Equal("Corrected", retained.Notes);
  }

  [Fact]
  public void SourceSequenceCannotOverrideAcceptedOrder()
  {
    var first = Stop(20);
    var second = Stop(10);
    var leg = new ExecutionLeg { Id = Guid.NewGuid() };
    ExecutionStopRows.Replace(leg, [first, second]);

    var visits = ExecutionStopRows.Read(leg);

    Assert.Equal(
      new[] { first.Id, second.Id },
      visits.OrderBy(x => x.Sequence).Select(x => x.Id)
    );
    Assert.Equal(new[] { 20, 10 }, new[] { first.Sequence, second.Sequence });
  }

  [Fact]
  public void DuplicateOccurrenceIsRejectedBeforeChangingTrackedRows()
  {
    var stop = Stop(1);
    var leg = new ExecutionLeg { Id = Guid.NewGuid() };
    ExecutionStopRows.Replace(leg, [stop]);
    var row = Assert.Single(leg.Stops);

    Assert.Throws<InvalidOperationException>(
      () => ExecutionStopRows.Replace(leg, [stop, stop])
    );

    Assert.Same(row, Assert.Single(leg.Stops));
    Assert.Equal(0, row.Position);
  }

  [Fact]
  public void LegOwnsResourcesAndReadingDoesNotMutateSavedFacts()
  {
    var stop = Stop(1);
    stop.TruckId = Guid.NewGuid();
    stop.DriverId = Guid.NewGuid();
    stop.CoDriverId = Guid.NewGuid();
    stop.TrailerId = Guid.NewGuid();
    stop.TruckNumber = "Source truck";
    var leg = new ExecutionLeg
    {
      TruckId = Guid.NewGuid(),
      DriverId = Guid.NewGuid(),
      CoDriverId = Guid.NewGuid(),
      TrailerId = Guid.NewGuid(),
      Stops = ExecutionStopRows.Capture([stop]),
    };

    var read = Assert.Single(ExecutionStopRows.Read(leg));

    Assert.Equal(leg.TruckId, read.TruckId);
    Assert.Equal(leg.DriverId, read.DriverId);
    Assert.Equal(leg.CoDriverId, read.CoDriverId);
    Assert.Equal(leg.TrailerId, read.TrailerId);
    Assert.Empty(read.TruckNumber);
    read.ExecutionCompleted = true;
    read.Notes = "Projection only";
    Assert.False(Assert.Single(leg.Stops).ExecutionCompleted);
    Assert.Empty(Assert.Single(leg.Stops).Notes);
  }

  [Theory]
  [InlineData("{")]
  [InlineData("{}")]
  [InlineData("[null]")]
  [InlineData("[1]")]
  public void ImmutableLegacyReceiptsStillRejectUnreadableVisits(string json)
  {
    Assert.Throws<JsonException>(() => ExecutionSnapshots.Read(json));
  }

  private static DispatchStop Stop(int sequence) =>
    new() { Id = Guid.NewGuid(), Sequence = sequence };
}
