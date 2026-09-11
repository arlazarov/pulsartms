using Application.Features.Routing.Algorithms;
using Domain.Entities.Dispatch;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
public sealed class NextLoadSelectionTests
{
  private static Dispatch Load(int day, string status = "assigned") => new()
  {
    Id = Guid.NewGuid(), Status = status,
    Stops = [new() { Sequence = 1, ScheduledDate = new(2026, 9, day) }]
  };

  [Fact]
  public void MissingCurrentIdStillExcludesActiveLoadAndOrdersUpcomingLoads()
  {
    var active = Load(7, "in_transit"); var first = Load(8); var last = Load(10);
    Assert.Equal(new[] { first.Id, last.Id }, NextLoadSelection.Select([last, active, first], null).Select(x => x.Id));
  }

  [Fact]
  public void SelectedAssignedLoadAndEarlierLoadsAreExcluded()
  {
    var earlier = Load(7); var selected = Load(8); var next = Load(9);
    Assert.Equal(next.Id, Assert.Single(NextLoadSelection.Select([next, selected, earlier], selected.Id)).Id);
  }

  [Fact]
  public void CompletedLoadsAreNeverReturned()
  {
    Assert.Empty(NextLoadSelection.Select([Load(7, "in_transit"), Load(8, "completed")], null));
  }
}
