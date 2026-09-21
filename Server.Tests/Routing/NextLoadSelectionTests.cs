using Application.Features.Routing.Algorithms;
using Domain.Entities.Dispatch;
using Domain.Rules;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class NextLoadSelectionTests
{
  private static Dispatch Load(int day, string status = "assigned") =>
    new()
    {
      Id = Guid.NewGuid(),
      Status = status,
      Stops = [new() { Sequence = 1, ScheduledDate = new(2026, 9, day) }],
    };

  [Fact]
  public void MissingCurrentIdStillExcludesActiveLoadAndOrdersUpcomingLoads()
  {
    var active = Load(7, "in_transit");
    var first = Load(8);
    var last = Load(10);
    Assert.Equal(
      new[] { first.Id, last.Id },
      NextLoadSelection.Select([last, active, first], null).Select(x => x.Id)
    );
  }

  [Fact]
  public void SelectedAssignedLoadAndEarlierLoadsAreExcluded()
  {
    var earlier = Load(7);
    var selected = Load(8);
    var next = Load(9);
    Assert.Equal(
      next.Id,
      Assert
        .Single(
          NextLoadSelection.Select([next, selected, earlier], selected.Id)
        )
        .Id
    );
  }

  [Fact]
  public void CompletedLoadsAreNeverReturned()
  {
    Assert.Empty(
      NextLoadSelection.Select(
        [Load(7, "in_transit"), Load(8, "completed")],
        null
      )
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void FollowingInTransitLoadIsRetainedWithoutDuplicatingCurrent(
    bool explicitCurrent
  )
  {
    var current = Load(7, "in_transit");
    var next = Load(8, "in_transit");
    var assigned = Load(9);
    var completed = Load(10, "completed");
    var cancelled = Load(11, "cancelled");
    Assert.Equal(
      new[] { next.Id, assigned.Id },
      NextLoadSelection
        .Select(
          [assigned, next, cancelled, current, completed],
          explicitCurrent ? current.Id : null
        )
        .Select(x => x.Id)
    );
  }

  [Fact]
  public void EarlierInTransitLoadsAndManuallyCompletedLoadsStayExcluded()
  {
    var earlier = Load(7, "in_transit");
    var current = Load(8, "in_transit");
    var completed = Load(9, "in_transit");
    completed.Stops[0].ManualCompletedAt = new DateTime(
      2026,
      9,
      9,
      12,
      0,
      0,
      DateTimeKind.Utc
    );
    var next = Load(10, "in_transit");
    Assert.Equal(
      next.Id,
      Assert
        .Single(
          NextLoadSelection.Select(
            [next, earlier, completed, current],
            current.Id
          )
        )
        .Id
    );
  }
}
