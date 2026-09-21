using System.Collections.Immutable;
using Domain.Models.Routing;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelRoadDependencyStoreTests
{
  [Fact]
  public async Task SummaryRoundTripKeepsDependenciesWithoutLoadingGeometry()
  {
    await using var f = await TruckFuelPlanFixture.CreateAsync();
    var original = f.Snapshot();
    var snapshot = original with
    {
      RoadDependencies = FuelRoadDependencies.Capture(
        [
          new(new(f.CurrentId, null), SavedRoadKind.Plan, new string('A', 64))
          {
            ProgressSignature = "publication-only progress",
          },
          new(new(f.FutureId, null), SavedRoadKind.Base, new string('B', 64)),
        ]
      ),
    };
    var store = new TruckFuelPlanStore(
      f.Db,
      NullLogger<TruckFuelPlanStore>.Instance
    );
    await store.SaveAsync(snapshot, default);
    f.Commands.Reads.Clear();

    var read = await store.ReadAsync(f.TruckId, false, default);

    Assert.NotNull(read!.RoadDependencies);
    Assert.Equal(
      snapshot.RoadDependencies.Roads.ToArray(),
      read.RoadDependencies.Roads.ToArray()
    );
    Assert.Null(read.CheckedRoute);
    Assert.DoesNotContain("CheckedRouteJson", Assert.Single(f.Commands.Reads));
    Assert.All(
      read.RoadDependencies.Roads,
      road => Assert.Null(road.ProgressSignature)
    );
    var remaining = FuelRoadDependencies.Remaining(
      read,
      new() { TruckId = f.TruckId, DispatchId = f.FutureId }
    );
    Assert.Equal(f.FutureId, Assert.Single(remaining!).Work.DispatchId);
  }

  // A saved road belongs to a load and carries that load's assignment. Read
  // as "the root's leg, or none", this refused every road of a load already
  // accepted into execution - which is how the work ahead of a truck is held
  // now, so no plan covering it could be kept at all.
  [Fact]
  public async Task ARoadOfAnAcceptedChainedLoadKeepsItsOwnLeg()
  {
    await using var f = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(
      f.Db,
      NullLogger<TruckFuelPlanStore>.Instance
    );
    var leg = Guid.NewGuid();
    var original = f.Snapshot();
    var snapshot = original with
    {
      Stops =
      [
        original.Stops[0],
        new(f.FutureId, original.Stops[^1].Stop, original.Stops[^1].EndMiles)
        {
          ExecutionLegId = leg,
          AssignmentRevision = 2,
        },
      ],
      RoadDependencies = FuelRoadDependencies.Capture(
        [
          new(new(f.CurrentId, null), SavedRoadKind.Plan, new string('A', 64)),
          new(new(f.FutureId, leg), SavedRoadKind.Base, new string('B', 64)),
        ]
      ),
    };

    Assert.True(await store.SaveAsync(snapshot, default));

    var read = await store.ReadAsync(f.TruckId, false, default);
    Assert.Equal(leg, read!.Stops[^1].ExecutionLegId);
    Assert.Equal(leg, read.RoadDependencies!.Roads[^1].Work.ExecutionLegId);
  }

  [Theory]
  [InlineData("empty")]
  [InlineData("unknown-kind")]
  [InlineData("foreign-work")]
  [InlineData("wrong-leg")]
  [InlineData("bad-signature")]
  [InlineData("progress")]
  [InlineData("no-root")]
  [InlineData("too-many")]
  public async Task InvalidEvidenceCannotReplaceAnExistingPlan(string change)
  {
    await using var f = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(
      f.Db,
      NullLogger<TruckFuelPlanStore>.Instance
    );
    var before = f.Snapshot();
    await store.SaveAsync(before, default);
    var road = new SavedRoadVersion(
      new(f.CurrentId, null),
      SavedRoadKind.Plan,
      new string('A', 64)
    );
    road = change switch
    {
      "unknown-kind" => road with { Kind = (SavedRoadKind)99 },
      "foreign-work" => road with { Work = new(Guid.NewGuid(), null) },
      "wrong-leg" => road with { Work = new(f.CurrentId, Guid.NewGuid()) },
      "bad-signature" => road with { Signature = "invalid" },
      "progress" => road with { ProgressSignature = "transient" },
      "no-root" => road with { Kind = SavedRoadKind.Base },
      _ => road,
    };
    var updated = f.Snapshot(TruckFuelPlanFixture.Now.AddMinutes(1)) with
    {
      RoadDependencies = new(
        1,
        change switch
        {
          "empty" => [],
          "too-many" => Enumerable.Repeat(road, 129).ToImmutableArray(),
          _ => [road],
        }
      ),
    };

    await Assert.ThrowsAsync<ArgumentException>(
      () => store.ReplaceAsync(updated, before.CalculatedAt, default)
    );

    Assert.Equal(
      before.CalculatedAt,
      (await store.ReadAsync(f.TruckId, false, default))!.CalculatedAt
    );
  }
}
