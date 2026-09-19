using Application.Features.Execution.Services;
using Domain.Entities.Execution;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class ExecutionOutboxTests
{
  [Fact]
  public void AssignmentChangesRetainExactLoadTruckLegAndRevision()
  {
    using var db = Context();
    var loadId = Guid.NewGuid();
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Revision = 8,
      Loads = [new() { DispatchId = loadId }],
    };
    var now = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
    ExecutionPlanningChanges.Enqueue(db, [leg, leg], now);
    var work = Assert.Single(db.ExecutionPlanningChanges.Local);
    Assert.Equal(loadId, work.DispatchId);
    Assert.Equal(leg.Id, work.ExecutionLegId);
    Assert.Equal(leg.TruckId, work.TruckId);
    Assert.Equal(8, work.AssignmentRevision);
    Assert.Equal(now, work.AvailableAt);
    Assert.False(work.MileageOnly);
  }

  [Fact]
  public void RouteChoicesCanRefreshMileageWithoutChangingAssignment()
  {
    using var db = Context();
    var load = new DispatchEntity
    {
      Id = Guid.NewGuid(),
      ExecutionLegId = Guid.NewGuid(),
      AssignmentRevision = 4,
      TruckId = Guid.NewGuid(),
    };
    var now = DateTime.UtcNow;
    ExecutionPlanningChanges.RouteSaved(db, load, now);
    ExecutionPlanningChanges.RouteSaved(db, load, now.AddSeconds(1));
    Assert.Equal(2, db.ExecutionPlanningChanges.Local.Count);
    Assert.All(
      db.ExecutionPlanningChanges.Local,
      work =>
      {
        Assert.True(work.MileageOnly);
        Assert.Equal(load.ExecutionLegId, work.ExecutionLegId);
        Assert.Equal(4, work.AssignmentRevision);
      }
    );
  }

  [Fact]
  public void LegacyRoutesDoNotProduceNativeMileageWork()
  {
    using var db = Context();
    ExecutionPlanningChanges.RouteSaved(
      db,
      new() { Id = Guid.NewGuid(), TruckId = Guid.NewGuid() },
      DateTime.UtcNow
    );
    Assert.Empty(db.ExecutionPlanningChanges.Local);
  }

  private static AppDbContext Context() =>
    new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only;Username=design")
        .Options
    );
}
