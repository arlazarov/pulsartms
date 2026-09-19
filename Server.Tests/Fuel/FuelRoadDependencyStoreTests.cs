using System.Collections.Immutable;
using Application.Features.Routing.Models;
using Infrastructure.Persistence;
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
    var store = new TruckFuelPlanStore(f.Db);
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
    var store = new TruckFuelPlanStore(f.Db);
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
