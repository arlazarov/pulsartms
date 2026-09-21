using Application.Features.Routing.Services.FuelPlanning;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelPlanMemoryTests
{
  [Fact]
  public async Task EstimatedAccessCachesTheBaselineWithoutARecalculatedRoad()
  {
    using var memory = new FuelPlanMemory();
    var snapshot = Snapshot();
    snapshot = snapshot with
    {
      Plan = new() { EstimatedStationAccess = true },
      BaselineRoute = snapshot.CheckedRoute,
      CheckedRoute = null,
    };
    var summary = snapshot with { BaselineRoute = null };
    var calls = 0;
    Task<TruckFuelPlanSnapshot?> Load()
    {
      calls++;
      return Task.FromResult<TruckFuelPlanSnapshot?>(snapshot);
    }

    var geometry = Assert.IsType<RouteGeometry>(
      await memory.LegAsync(summary, 1, Load, default)
    );
    Assert.Equal(200, geometry.Miles, 8);
    Assert.Equal(snapshot.BaselineRoute!.Legs[1].Points[0], geometry.At(0));
    Assert.Equal(
      snapshot.BaselineRoute.Legs[1].Points[^1],
      geometry.At(geometry.Miles)
    );
    Assert.Same(geometry, await memory.LegAsync(summary, 1, Load, default));
    Assert.Equal(1, calls);
  }

  [Fact]
  public async Task LegacyPlanKeepsItsCheckedRoadEvenWhenABaselineIsPresent()
  {
    using var memory = new FuelPlanMemory();
    var snapshot = Snapshot();
    snapshot = snapshot with
    {
      BaselineRoute = new()
      {
        Legs = [new(70, 4200, [new(41, -80), new(41, -79)])],
      },
    };
    var geometry = Assert.IsType<RouteGeometry>(
      await memory.LegAsync(
        snapshot,
        0,
        () => Task.FromResult<TruckFuelPlanSnapshot?>(snapshot),
        default
      )
    );
    Assert.Equal(100, geometry.Miles, 8);
    Assert.Equal(snapshot.CheckedRoute!.Legs[0].Points[0], geometry.At(0));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task MissingAuthoritativeRouteDoesNotFallBackToTheOtherRouteKind(
    bool estimated
  )
  {
    using var memory = new FuelPlanMemory();
    var snapshot = Snapshot();
    snapshot = snapshot with
    {
      Plan = new() { EstimatedStationAccess = estimated },
      BaselineRoute = estimated ? null : snapshot.CheckedRoute,
      CheckedRoute = estimated ? snapshot.CheckedRoute : null,
    };
    Assert.Null(
      await memory.LegAsync(
        snapshot,
        0,
        () => Task.FromResult<TruckFuelPlanSnapshot?>(snapshot),
        default
      )
    );
  }

  [Fact]
  public async Task ANewEstimatedVersionReplacesOnlyItsOwnLegacyCachedLeg()
  {
    using var memory = new FuelPlanMemory();
    var legacy = Snapshot();
    var first = await memory.LegAsync(
      legacy,
      0,
      () => Task.FromResult<TruckFuelPlanSnapshot?>(legacy),
      default
    );
    var estimated = legacy with
    {
      CalculatedAt = legacy.CalculatedAt.AddMinutes(1),
      Plan = new() { EstimatedStationAccess = true },
      CheckedRoute = null,
      BaselineRoute = new()
      {
        Legs = [new(70, 4200, [new(41, -80), new(41, -79)])],
      },
    };
    var replacement = await memory.LegAsync(
      estimated,
      0,
      () => Task.FromResult<TruckFuelPlanSnapshot?>(estimated),
      default
    );
    Assert.NotSame(first, replacement);
    Assert.Equal(70, replacement!.Miles, 8);
    await memory.LegAsync(
      legacy,
      0,
      () => Task.FromResult<TruckFuelPlanSnapshot?>(legacy),
      default
    );
    Assert.Same(
      replacement,
      await memory.LegAsync(
        estimated,
        0,
        () =>
          throw new InvalidOperationException(
            "The current estimate must remain cached."
          ),
        default
      )
    );
  }

  [Fact]
  public async Task ColdLoadsAreBoundedWhileCacheHitsAndCancellationRemainAvailable()
  {
    using var memory = new FuelPlanMemory();
    var stripes = new HashSet<uint>();
    var snapshots = new List<TruckFuelPlanSnapshot>();
    while (snapshots.Count < 4)
    {
      var snapshot = Snapshot();
      if (stripes.Add((uint)snapshot.TruckId.GetHashCode() % 64))
        snapshots.Add(snapshot);
    }
    var hot = await memory.LegAsync(
      snapshots[0],
      0,
      () => Task.FromResult<TruckFuelPlanSnapshot?>(snapshots[0]),
      default
    );
    var opened = 0;
    var started = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var release = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    async Task<TruckFuelPlanSnapshot?> Load(TruckFuelPlanSnapshot snapshot)
    {
      if (Interlocked.Increment(ref opened) == 2)
        started.TrySetResult();
      await release.Task;
      return snapshot;
    }
    var first = memory.LegAsync(
      snapshots[1],
      0,
      () => Load(snapshots[1]),
      default
    );
    var second = memory.LegAsync(
      snapshots[2],
      0,
      () => Load(snapshots[2]),
      default
    );
    try
    {
      await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
      using var cancellation = new CancellationTokenSource();
      var waiting = memory.LegAsync(
        snapshots[3],
        0,
        () => Load(snapshots[3]),
        cancellation.Token
      );
      Assert.Equal(2, Volatile.Read(ref opened));
      Assert.False(waiting.IsCompleted);
      Assert.Same(
        hot,
        await memory.LegAsync(
          snapshots[0],
          0,
          () => throw new InvalidOperationException(),
          default
        )
      );
      cancellation.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
      Assert.Equal(2, Volatile.Read(ref opened));
    }
    finally
    {
      release.TrySetResult();
      await Task.WhenAll(first, second);
    }
    Assert.NotNull(
      await memory.LegAsync(snapshots[3], 0, () => Load(snapshots[3]), default)
    );
    Assert.Equal(3, opened);
  }

  [Fact]
  public async Task ATruckRetainsOnlyItsCurrentLegAndOlderReadsCannotReplaceNewerGeometry()
  {
    using var memory = new FuelPlanMemory();
    var original = Snapshot();
    var newer = original with
    {
      CalculatedAt = original.CalculatedAt.AddMinutes(1),
    };
    var calls = 0;
    Task<TruckFuelPlanSnapshot?> Load(TruckFuelPlanSnapshot snapshot)
    {
      calls++;
      return Task.FromResult<TruckFuelPlanSnapshot?>(snapshot);
    }

    var first = await memory.LegAsync(
      original,
      0,
      () => Load(original),
      default
    );
    var nextLeg = await memory.LegAsync(
      original,
      1,
      () => Load(original),
      default
    );
    var firstAgain = await memory.LegAsync(
      original,
      0,
      () => Load(original),
      default
    );
    Assert.NotSame(first, firstAgain);
    Assert.Equal(3, calls);
    Assert.Equal(100, firstAgain!.Miles, 8);
    Assert.Equal(200, nextLeg!.Miles, 8);

    var latest = await memory.LegAsync(newer, 1, () => Load(newer), default);
    await memory.LegAsync(original, 0, () => Load(original), default);
    Assert.Same(
      latest,
      await memory.LegAsync(newer, 1, () => Load(newer), default)
    );
    Assert.Equal(5, calls);
  }

  [Fact]
  public async Task DifferentTrucksRemainIndependentAndFailedRefreshKeepsTheValidLeg()
  {
    using var memory = new FuelPlanMemory();
    var first = Snapshot();
    var second = Snapshot();
    var calls = 0;
    Task<TruckFuelPlanSnapshot?> Load(TruckFuelPlanSnapshot snapshot)
    {
      calls++;
      return Task.FromResult<TruckFuelPlanSnapshot?>(snapshot);
    }
    var firstGeometry = await memory.LegAsync(
      first,
      0,
      () => Load(first),
      default
    );
    var secondGeometry = await memory.LegAsync(
      second,
      1,
      () => Load(second),
      default
    );
    var changed = first with
    {
      CalculatedAt = first.CalculatedAt.AddMinutes(1),
    };
    Assert.Null(await memory.LegAsync(changed, 0, () => Load(first), default));
    Assert.Same(
      firstGeometry,
      await memory.LegAsync(first, 0, () => Load(first), default)
    );
    Assert.Same(
      secondGeometry,
      await memory.LegAsync(second, 1, () => Load(second), default)
    );
    Assert.Equal(3, calls);
  }

  private static TruckFuelPlanSnapshot Snapshot() =>
    new(
      Guid.NewGuid(),
      Guid.NewGuid(),
      new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc),
      new(),
      [],
      new()
      {
        Legs =
        [
          new(100, 6000, [new(40, -80), new(40, -79)]),
          new(200, 12000, [new(40, -79), new(40, -77)]),
        ],
      }
    );
}
