using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Unit")]
public sealed class ExecutionSourceTopologyTests
{
  [Fact]
  public void SourceTransfersAndHistoricalTruckAssignmentsAreNotNewVisits()
  {
    var f = Fixture();
    f.Legs[0].TruckId = f.Legs[1].TruckId;
    var result = Resolve(f);

    Assert.False(result.NeedsReview);
    Assert.Empty(result.ChangedLegIds);
    Assert.Equal(
      new[] { f.Native[1].Id, f.Source.Stops[3].Id },
      result.Stops[f.Legs[1].Id].Select(x => x.Id)
    );
    Assert.Equal(f.Native[0].Id, result.Stops[f.Legs[0].Id][^1].Id);
  }

  [Fact]
  public void AppendedDeliveryUsesTerminalActiveLegAndKeepsHookFirst()
  {
    var f = Fixture();
    var added = Stop(5, "Drop Off", f.Legs[1].TruckId);
    f.Source.Stops.Add(added);
    var original = ExecutionSnapshots.Write(f.Snapshots[f.Legs[1].Id]);
    var result = Resolve(f);

    Assert.False(result.NeedsReview);
    Assert.Equal(f.Legs[1].Id, Assert.Single(result.ChangedLegIds));
    Assert.Equal(
      new[] { f.Native[1].Id, f.Source.Stops[3].Id, added.Id },
      result.Stops[f.Legs[1].Id].Select(x => x.Id)
    );
    Assert.Equal(original, ExecutionSnapshots.Write(f.Snapshots[f.Legs[1].Id]));
    Assert.Equal(7, f.Legs[1].Revision);
  }

  [Fact]
  public void AddedFutureVisitStaysBetweenItsSavedNeighbors()
  {
    var f = Fixture();
    var end = Stop(6, "Drop Off", f.Legs[1].TruckId);
    f.Source.Stops.Add(end);
    f.Snapshots[f.Legs[1].Id].Add(ExecutionSnapshots.Copy(end));
    var added = Stop(5, "Waypoint", f.Legs[1].TruckId);
    f.Source.Stops.Add(added);
    var result = Resolve(f);

    Assert.False(result.NeedsReview);
    Assert.Equal(
      new[] { f.Native[1].Id, f.Source.Stops[3].Id, added.Id, end.Id },
      result.Stops[f.Legs[1].Id].Select(x => x.Id)
    );
  }

  [Theory]
  [InlineData("reordered")]
  [InlineData("removed-history")]
  [InlineData("completed-leg")]
  [InlineData("actual-added")]
  [InlineData("before-recorded")]
  [InlineData("different-truck")]
  [InlineData("number-only-truck")]
  [InlineData("different-driver")]
  [InlineData("different-codriver")]
  [InlineData("different-trailer")]
  [InlineData("new-transfer")]
  [InlineData("between-transfers")]
  public void AmbiguousOrHistoricalChangesRetainAllSnapshots(string mode)
  {
    var f = Fixture();
    var added = Stop(5, "Waypoint", f.Legs[1].TruckId);
    f.Source.Stops.Add(added);
    switch (mode)
    {
      case "reordered":
        f.Source.Stops[0].Sequence = 10;
        break;
      case "removed-history":
        f.Source.Stops.RemoveAt(0);
        break;
      case "completed-leg":
        f.Legs[1].Status = "completed";
        break;
      case "actual-added":
        added.ArrivedAt = DateTime.UtcNow;
        break;
      case "before-recorded":
        f.Source.Stops[3].Sequence = 6;
        f.Snapshots[f.Legs[1].Id][1].ArrivedAt = DateTime.UtcNow;
        break;
      case "different-truck":
        added.TruckId = Guid.NewGuid();
        break;
      case "number-only-truck":
        added.TruckId = null;
        added.TruckNumber = "another-truck";
        break;
      case "different-driver":
        added.DriverId = Guid.NewGuid();
        break;
      case "different-codriver":
        added.CoDriverId = Guid.NewGuid();
        break;
      case "different-trailer":
        added.TrailerId = Guid.NewGuid();
        break;
      case "new-transfer":
        added.Job = "Drop";
        break;
      case "between-transfers":
        f.Source.Stops[2].Sequence = 6;
        f.Source.Stops[3].Sequence = 7;
        break;
    }
    var original = f.Snapshots.ToDictionary(
      x => x.Key,
      x => ExecutionSnapshots.Write(x.Value)
    );
    var result = Resolve(f);

    Assert.True(result.NeedsReview);
    Assert.Empty(result.ChangedLegIds);
    Assert.All(
      result.Stops,
      entry =>
        Assert.Equal(original[entry.Key], ExecutionSnapshots.Write(entry.Value))
    );
  }

  private static ExecutionSourceTopology Resolve(TopologyFixture fixture) =>
    ExecutionSourceTopology.Resolve(
      fixture.Source,
      fixture.Legs,
      fixture.Snapshots,
      fixture.Native
    );

  private static TopologyFixture Fixture()
  {
    var truck = Guid.NewGuid();
    var source = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      Stops =
      [
        Stop(1, "Pick Up", truck),
        Stop(2, "Drop", truck),
        Stop(3, "Hook", truck),
        Stop(4, "Drop Off", truck),
      ],
    };
    var native = new[]
    {
      new ExecutionTransferVisit
      {
        Id = Guid.NewGuid(),
        SourceDispatchStopId = source.Stops[1].Id,
        Operation = "Drop",
        SiteName = "Saved transfer address",
      },
      new ExecutionTransferVisit
      {
        Id = Guid.NewGuid(),
        SourceDispatchStopId = source.Stops[2].Id,
        Operation = "Hook",
        SiteName = "Saved transfer address",
      },
    };
    var transfer = Guid.NewGuid();
    var legs = new[]
    {
      new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        TruckId = Guid.NewGuid(),
        Status = "completed",
        EndSwitchId = transfer,
        Revision = 3,
        Loads = [new() { DispatchId = source.Id }],
      },
      new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        TruckId = truck,
        Status = "active",
        StartSwitchId = transfer,
        Revision = 7,
        Loads = [new() { DispatchId = source.Id }],
      },
    };
    var snapshots = new Dictionary<Guid, List<DispatchStop>>
    {
      [legs[0].Id] =
      [
        ExecutionSnapshots.Copy(source.Stops[0]),
        ExecutionSnapshots.Boundary(native[0], source.Id, 2, "Loaded"),
      ],
      [legs[1].Id] =
      [
        ExecutionSnapshots.Boundary(native[1], source.Id, 0, "Loaded"),
        ExecutionSnapshots.Copy(source.Stops[3]),
      ],
    };
    return new(source, legs, snapshots, native);
  }

  private static DispatchStop Stop(int sequence, string job, Guid truck) =>
    new()
    {
      Id = Guid.NewGuid(),
      Sequence = sequence,
      Job = job,
      TruckId = truck,
      Address = $"{sequence} Main Street",
    };

  private sealed record TopologyFixture(
    DispatchEntity Source,
    ExecutionLeg[] Legs,
    Dictionary<Guid, List<DispatchStop>> Snapshots,
    ExecutionTransferVisit[] Native
  );
}
