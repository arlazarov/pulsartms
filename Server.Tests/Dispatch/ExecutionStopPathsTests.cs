using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionStopPathsTests
{
  [Fact]
  public void InsertionChangesTheCrossingPathButNotItsNeighbours()
  {
    var before = Stops();
    var after = before.Select(ExecutionSnapshots.Copy).ToList();
    after.Insert(2, new() { Id = Guid.NewGuid(), Address = "Same facility" });

    var changes = ExecutionStopPaths.Compare(before, after);

    Assert.True(changes.HasChanges);
    Assert.True(changes.Affects(before[1].Id, before[2].Id));
    Assert.True(changes.Affects(before[0].Id, before[3].Id));
    Assert.False(changes.Affects(before[0].Id, before[1].Id));
    Assert.False(changes.Affects(before[2].Id, before[3].Id));
    Assert.False(changes.Affects(null, before[0].Id));
    Assert.False(changes.Affects(before[^1].Id, null));
  }

  [Fact]
  public void InteriorLocationChangeAffectsEvidenceSpanningThatVisit()
  {
    var before = Stops();
    var after = before.Select(ExecutionSnapshots.Copy).ToList();
    after[1].Latitude = 35;
    after[1].Longitude = -80;

    var changes = ExecutionStopPaths.Compare(before, after);

    Assert.True(changes.Affects(before[0].Id, before[3].Id));
    Assert.True(changes.Affects(before[1].Id, before[2].Id));
    Assert.False(changes.Affects(before[2].Id, before[3].Id));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void BoundaryExtensionChangesOnlyItsConnection(bool prefix)
  {
    var before = Stops();
    var after = before.Select(ExecutionSnapshots.Copy).ToList();
    after.Insert(prefix ? 0 : after.Count, new() { Id = Guid.NewGuid() });

    var changes = ExecutionStopPaths.Compare(before, after);

    Assert.Equal(prefix, changes.Affects(Guid.NewGuid(), before[0].Id));
    Assert.Equal(!prefix, changes.Affects(before[^1].Id, Guid.NewGuid()));
    Assert.False(changes.Affects(before[0].Id, before[^1].Id));
  }

  [Fact]
  public void AppointmentAndActualChangesDoNotInvalidateThePhysicalPath()
  {
    var before = Stops();
    var after = before.Select(ExecutionSnapshots.Copy).ToList();
    after[1].ScheduledDate = new(2026, 9, 20);
    after[1].ArrivedAt = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
    after[1].Sequence = 20;

    var changes = ExecutionStopPaths.Compare(before, after);

    Assert.False(changes.HasChanges);
    Assert.False(changes.Affects(before[0].Id, before[^1].Id));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void RemovedOrReorderedOccurrencesInvalidateOnlyTheirPaths(bool remove)
  {
    var before = Stops();
    var after = before.Select(ExecutionSnapshots.Copy).ToList();
    if (remove)
      after.RemoveAt(1);
    else
      (after[0], after[1]) = (after[1], after[0]);

    var changes = ExecutionStopPaths.Compare(before, after);

    Assert.True(changes.Affects(before[0].Id, before[2].Id));
    Assert.True(changes.Affects(before[0].Id, before[1].Id));
    Assert.False(changes.Affects(before[2].Id, before[3].Id));
  }

  [Fact]
  public void UnanchoredEvidenceCannotEstablishAnUnaffectedPartOfTheLeg()
  {
    var before = Stops();
    var after = before.Select(ExecutionSnapshots.Copy).ToList();
    after[1].Address = "Different facility";

    var changes = ExecutionStopPaths.Compare(before, after);

    Assert.True(changes.Affects(null, null));
    Assert.True(changes.Affects(Guid.NewGuid(), Guid.NewGuid()));
  }

  private static List<DispatchStop> Stops() =>
    Enumerable
      .Range(0, 4)
      .Select(i => new DispatchStop
      {
        Id = Guid.NewGuid(),
        Sequence = i + 1,
        Job = "Waypoint",
        Address = "Same facility",
      })
      .ToList();
}
