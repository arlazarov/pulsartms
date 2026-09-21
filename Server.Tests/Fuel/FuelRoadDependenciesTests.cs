using Domain.Models.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelRoadDependenciesTests
{
  [Fact]
  public void RemainingScopeDropsCompletedRootAndKeepsOnwardConnection()
  {
    var truck = Guid.NewGuid();
    var root = Guid.NewGuid();
    var next = Guid.NewGuid();
    var onward = Guid.NewGuid();
    SavedRoadVersion Road(Guid work, SavedRoadKind kind) =>
      new(new(work, null), kind, new string('A', 64));
    var saved = new TruckFuelPlanSnapshot(
      truck,
      root,
      DateTime.UtcNow,
      new()
      {
        TruckId = truck,
        DispatchIds = [root, next],
        ArrivalPolicy = new() { NextDispatchId = onward },
      },
      [new(next, new(Guid.NewGuid(), "", "", 1, new(40, -80)), 100)],
      null
    )
    {
      RoadDependencies = FuelRoadDependencies.Capture(
        [
          Road(root, SavedRoadKind.Plan),
          Road(next, SavedRoadKind.Base),
          Road(next, SavedRoadKind.Connection),
          Road(onward, SavedRoadKind.Connection),
        ]
      ),
    };
    var current = new RoutePlan { TruckId = truck, DispatchId = next };

    var remaining = FuelRoadDependencies.Remaining(saved, current)!;

    Assert.Equal(3, remaining.Count);
    Assert.DoesNotContain(remaining, x => x.Work.DispatchId == root);
    Assert.Contains(remaining, x => x.Work.DispatchId == onward);
    current.ExecutionLegId = Guid.NewGuid();
    Assert.Null(FuelRoadDependencies.Remaining(saved, current));
  }
}
