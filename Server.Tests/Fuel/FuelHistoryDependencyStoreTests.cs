using System.Collections.Immutable;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelHistoryDependencyStoreTests
{
  [Fact]
  public async Task SummaryRetainsLookupSeedsWithoutPredecessorsOrGeometry()
  {
    await using var f = await TruckFuelPlanFixture.CreateAsync();
    var input = Input(f);
    var snapshot = f.Snapshot() with
    {
      HistoryDependencies = new(1, [new([input])]),
    };
    var store = new TruckFuelPlanStore(
      f.Db,
      NullLogger<TruckFuelPlanStore>.Instance
    );
    await store.SaveAsync(snapshot, default);
    f.Commands.Reads.Clear();

    var saved = await store.ReadAsync(f.TruckId, false, default);

    var batch = Assert.Single(saved!.HistoryDependencies!.Batches);
    var read = Assert.Single(batch.Inputs);
    Assert.Equal(input.InputSignature, read.InputSignature);
    Assert.Equal(input.Current.Id, read.Current.Id);
    Assert.Equal(input.Current.Stops.ToArray(), read.Current.Stops.ToArray());
    Assert.DoesNotContain("CheckedRouteJson", Assert.Single(f.Commands.Reads));
    Assert.Null(saved.CheckedRoute);
  }

  [Theory]
  [InlineData("default")]
  [InlineData("empty-batch")]
  [InlineData("null-input")]
  [InlineData("signature")]
  [InlineData("foreign-work")]
  [InlineData("foreign-truck")]
  [InlineData("missing-stops")]
  [InlineData("duplicate-input")]
  [InlineData("too-many-batches")]
  public async Task InvalidHistoryCannotReplaceTheAcceptedPlan(string change)
  {
    await using var f = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(
      f.Db,
      NullLogger<TruckFuelPlanStore>.Instance
    );
    var before = f.Snapshot();
    await store.SaveAsync(before, default);
    var input = Input(f);
    input = change switch
    {
      "null-input" => null!,
      "signature" => input with { InputSignature = "unknown" },
      "foreign-work" => input with
      {
        Current = input.Current with { Id = Guid.NewGuid() },
      },
      "foreign-truck" => input with
      {
        Current = input.Current with { TruckId = Guid.NewGuid() },
      },
      "missing-stops" => input with
      {
        Current = input.Current with { Stops = default },
      },
      _ => input,
    };
    var updated = f.Snapshot(TruckFuelPlanFixture.Now.AddMinutes(1)) with
    {
      HistoryDependencies = new(
        1,
        change switch
        {
          "default" => default,
          "empty-batch" => [new([])],
          "duplicate-input" => [new([input, input])],
          "too-many-batches" => Enumerable
            .Repeat(new FuelHistoryBatch([input]), 129)
            .ToImmutableArray(),
          _ => [new([input])],
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

  private static FuelHistoryInput Input(TruckFuelPlanFixture f) =>
    new(
      RouteWorkProjection.Capture(
        new DispatchEntity
        {
          Id = f.FutureId,
          TruckId = f.TruckId,
          Stops =
          [
            new()
            {
              Id = Guid.NewGuid(),
              Sequence = 1,
              Latitude = 40,
              Longitude = -80,
            },
          ],
        }
      ),
      new string('A', 64)
    );
}
