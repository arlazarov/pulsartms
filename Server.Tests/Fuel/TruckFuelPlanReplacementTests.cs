using Application.Features.Routing.Models;
using Domain.Entities.Fuel;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class TruckFuelPlanReplacementTests
{
  [Fact]
  public async Task MissingExpectedRevisionOnlyInsertsTheFirstPlan()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();

    Assert.True(await store.ReplaceAsync(original, null, default));
    Assert.False(
      await store.ReplaceAsync(
        fixture.Snapshot(original.CalculatedAt.AddMinutes(1)),
        null,
        default
      )
    );

    await AssertSnapshotAsync(fixture, original);
    Assert.Equal(1, await fixture.Db.Set<TruckFuelPlan>().CountAsync());
  }

  [Fact]
  public async Task ExpectedRevisionDoesNotInsertWhenThePlanNoLongerExists()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);

    Assert.False(
      await store.ReplaceAsync(
        fixture.Snapshot(TruckFuelPlanFixture.Now.AddMinutes(1)),
        TruckFuelPlanFixture.Now,
        default
      )
    );
    Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
  }

  [Fact]
  public async Task MatchingRevisionReplacesAllColumnsWithoutAccumulatingHistory()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();
    Assert.True(await store.SaveAsync(original, default));
    var changed = fixture.Snapshot(original.CalculatedAt.AddMinutes(1));
    changed.Plan.ManuallyEdited = true;
    changed.Plan.DispatchIds = [fixture.FutureId, fixture.CurrentId];
    changed.Plan.Stops[0].DispatchId = fixture.CurrentId;
    changed.Plan.Stops[0].FillToTarget = true;
    changed.Plan.Notes = ["Manual replacement"];
    changed.CheckedRoute!.Warnings = ["Replacement geometry"];
    changed = changed with
    {
      RootDispatchId = fixture.FutureId,
      Stops =
      [
        changed.Stops[0] with
        {
          DispatchId = fixture.FutureId,
        },
        changed.Stops[1] with
        {
          DispatchId = fixture.CurrentId,
        },
      ],
    };

    Assert.True(
      await store.ReplaceAsync(changed, original.CalculatedAt, default)
    );

    await AssertSnapshotAsync(fixture, changed);
    Assert.Equal(1, await fixture.Db.Set<TruckFuelPlan>().CountAsync());
  }

  [Fact]
  public async Task StaleExpectedRevisionCannotOverwriteEvenWithALaterCandidateTimestamp()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();
    Assert.True(await store.ReplaceAsync(original, null, default));
    var winner = fixture.Snapshot(original.CalculatedAt.AddMinutes(1));
    Assert.True(
      await store.ReplaceAsync(winner, original.CalculatedAt, default)
    );

    Assert.False(
      await store.ReplaceAsync(
        fixture.Snapshot(original.CalculatedAt.AddMinutes(2)),
        original.CalculatedAt,
        default
      )
    );

    await AssertSnapshotAsync(fixture, winner);
  }

  [Fact]
  public async Task WriterPausedBeforeItsUpdateCannotOverwriteAnInterleavedEdit()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();
    Assert.True(await store.ReplaceAsync(original, null, default));
    await using var competingContext = new AppDbContext(fixture.Options);
    var winner = fixture.Snapshot(original.CalculatedAt.AddMinutes(1));
    fixture.Commands.BeforeWrite = async () =>
      Assert.True(
        await new TruckFuelPlanStore(competingContext).ReplaceAsync(
          winner,
          original.CalculatedAt,
          default
        )
      );

    Assert.False(
      await store.ReplaceAsync(
        fixture.Snapshot(original.CalculatedAt.AddMinutes(2)),
        original.CalculatedAt,
        default
      )
    );

    Assert.Null(fixture.Commands.BeforeWrite);
    await AssertSnapshotAsync(fixture, winner);
  }

  [Fact]
  public async Task InitialWriterPausedBeforeItsInsertCannotOverwriteAnInterleavedFirstPlan()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    await using var competingContext = new AppDbContext(fixture.Options);
    var winner = fixture.Snapshot();
    fixture.Commands.BeforeWrite = async () =>
      Assert.True(
        await new TruckFuelPlanStore(competingContext).ReplaceAsync(
          winner,
          null,
          default
        )
      );

    Assert.False(
      await store.ReplaceAsync(
        fixture.Snapshot(winner.CalculatedAt.AddMinutes(1)),
        null,
        default
      )
    );

    Assert.Null(fixture.Commands.BeforeWrite);
    await AssertSnapshotAsync(fixture, winner);
  }

  [Fact]
  public async Task ExpectedRevisionUsesDatabaseMicrosecondsWhileJsonRetainsTheOriginalInstant()
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot(TruckFuelPlanFixture.Now.AddTicks(7));
    Assert.True(await store.ReplaceAsync(original, null, default));
    var changed = fixture.Snapshot(TruckFuelPlanFixture.Now.AddTicks(19));

    Assert.True(
      await store.ReplaceAsync(changed, original.CalculatedAt, default)
    );

    await AssertSnapshotAsync(fixture, changed);
    var row = await fixture
      .Db.Set<TruckFuelPlan>()
      .AsNoTracking()
      .SingleAsync();
    Assert.Equal(TruckFuelPlanFixture.Now.AddTicks(10), row.CalculatedAt);
  }

  [Theory]
  [InlineData(-10)]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(9)]
  public async Task CandidateMustBeNewerAtDatabasePrecision(int ticks)
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();
    Assert.True(await store.ReplaceAsync(original, null, default));

    Assert.False(
      await store.ReplaceAsync(
        fixture.Snapshot(original.CalculatedAt.AddTicks(ticks)),
        original.CalculatedAt,
        default
      )
    );

    await AssertSnapshotAsync(fixture, original);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ReplaceParticipatesInTheCallerTransactionAndRollsBackTogether(
    bool existingPlan
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();
    if (existingPlan)
      Assert.True(await store.ReplaceAsync(original, null, default));
    var changed = fixture.Snapshot(original.CalculatedAt.AddMinutes(1));
    await using (
      var transaction = await fixture.Db.Database.BeginTransactionAsync()
    )
    {
      var truck = await fixture.Db.Trucks.SingleAsync();
      truck.UnitNumber = "Pending companion write";
      await fixture.Db.SaveChangesAsync();
      Assert.True(
        await store.ReplaceAsync(
          changed,
          existingPlan ? original.CalculatedAt : null,
          default
        )
      );
      await AssertSnapshotAsync(fixture, changed);
      Assert.Same(transaction, fixture.Db.Database.CurrentTransaction);
      await transaction.RollbackAsync();
    }

    fixture.Db.ChangeTracker.Clear();
    Assert.Equal(
      string.Empty,
      (await fixture.Db.Trucks.SingleAsync()).UnitNumber
    );
    if (existingPlan)
      await AssertSnapshotAsync(fixture, original);
    else
      Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
  }

  [Theory]
  [InlineData("non-utc-revision")]
  [InlineData("default-revision")]
  [InlineData("identity")]
  [InlineData("geometry")]
  [InlineData("summary-size")]
  [InlineData("geometry-size")]
  [InlineData("cancellation")]
  public async Task ReplacementPreservesValidationBoundsAndCancellationWithoutMutatingTheSavedPlan(
    string failure
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = fixture.Snapshot();
    Assert.True(await store.ReplaceAsync(original, null, default));
    var changed = fixture.Snapshot(original.CalculatedAt.AddMinutes(1));
    var expected = original.CalculatedAt;
    using var cancellation = new CancellationTokenSource();
    switch (failure)
    {
      case "non-utc-revision":
        expected = DateTime.SpecifyKind(expected, DateTimeKind.Unspecified);
        break;
      case "default-revision":
        expected = DateTime.SpecifyKind(default, DateTimeKind.Utc);
        break;
      case "identity":
        changed.Plan.TruckId = Guid.NewGuid();
        break;
      case "geometry":
        changed.CheckedRoute!.Miles++;
        break;
      case "summary-size":
        changed.Plan.Notes = [new string('x', 512 * 1024)];
        break;
      case "geometry-size":
        changed.CheckedRoute!.Warnings = [new string('x', 8 * 1024 * 1024)];
        break;
      case "cancellation":
        cancellation.Cancel();
        break;
    }

    if (failure == "cancellation")
      await Assert.ThrowsAnyAsync<OperationCanceledException>(
        () => store.ReplaceAsync(changed, expected, cancellation.Token)
      );
    else
      await Assert.ThrowsAsync<ArgumentException>(
        () => store.ReplaceAsync(changed, expected, default)
      );

    await AssertSnapshotAsync(fixture, original);
  }

  private static async Task AssertSnapshotAsync(
    TruckFuelPlanFixture fixture,
    TruckFuelPlanSnapshot expected
  )
  {
    var actual = Assert.IsType<TruckFuelPlanSnapshot>(
      await new TruckFuelPlanStore(fixture.Db).ReadAsync(
        fixture.TruckId,
        true,
        default
      )
    );
    Assert.Equal(expected.CalculatedAt, actual.CalculatedAt);
    Assert.Equal(expected.RootDispatchId, actual.RootDispatchId);
    Assert.Equal(expected.Plan.DispatchIds, actual.Plan.DispatchIds);
    Assert.Equal(expected.Plan.ManuallyEdited, actual.Plan.ManuallyEdited);
    Assert.Equal(expected.Stops, actual.Stops);
    Assert.Equal(
      expected.Plan.Stops[0].StationId,
      actual.Plan.Stops[0].StationId
    );
    Assert.Equal(
      expected.Plan.Stops[0].FillToTarget,
      actual.Plan.Stops[0].FillToTarget
    );
    Assert.Equal(expected.Plan.Notes, actual.Plan.Notes);
    Assert.Equal(
      expected.CheckedRoute!.Legs.SelectMany(x => x.Points),
      actual.CheckedRoute!.Legs.SelectMany(x => x.Points)
    );
    Assert.Equal(expected.CheckedRoute.Warnings, actual.CheckedRoute.Warnings);
  }
}
