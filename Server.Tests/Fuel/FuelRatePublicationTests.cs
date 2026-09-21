using Application.Features.Fuel.Models;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelRatePublicationTests
{
  [Theory]
  [InlineData("busy")]
  [InlineData("commit")]
  [InlineData("cancel")]
  public async Task FailedRatePublicationPreservesStorageAndWarmCache(
    string failure
  )
  {
    await using var f = await FuelRatePublicationFixture.CreateAsync();
    var previous = await f.SeedAsync();
    Assert.Equal(previous, await f.Service.ReadAsync(default));
    var generation = f.Reads.Generation("fuel-exchange-rate");
    using var cancellation = new CancellationTokenSource();
    f.Provider.Read = _ =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      return Task.FromResult(Current(.79m));
    };
    f.Publication.BeforeBegin = () =>
    {
      Assert.Equal(1, f.Provider.Calls);
      Assert.Equal(generation, f.Reads.Generation("fuel-exchange-rate"));
      if (failure == "busy")
        throw new RoutePlanningException("Busy", DateTime.UtcNow.AddSeconds(5));
      if (failure == "cancel")
        cancellation.Cancel();
      f.Failure.FailNextCommit = failure == "commit";
      return Task.CompletedTask;
    };

    var error = await Record.ExceptionAsync(
      () => f.Service.RefreshAsync(cancellation.Token)
    );

    Assert.NotNull(error);
    if (failure == "busy")
      Assert.IsType<RoutePlanningException>(error);
    else if (failure == "commit")
      Assert.IsType<InvalidOperationException>(error);
    else
      Assert.IsAssignableFrom<OperationCanceledException>(error);
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(previous, await f.Store.ReadAsync(default));
    Assert.Equal(previous, await f.Service.ReadAsync(default));
    Assert.Equal(generation, f.Reads.Generation("fuel-exchange-rate"));
    Assert.Equal(
      "",
      await f.Db.SynchronizationCheckpoints.Select(x => x.Owner).SingleAsync()
    );

    f.Publication.BeforeBegin = null;
    Assert.True(await f.Service.RefreshAsync(default));
    Assert.Equal(.79m, (await f.Service.ReadAsync(default))!.UsdPerCad);
    Assert.Equal(2, f.Provider.Calls);
    Assert.NotEqual(generation, f.Reads.Generation("fuel-exchange-rate"));
  }

  [Fact]
  public async Task ProviderRunsBeforeTheOwnedWriteAndCacheChangesAfterCommit()
  {
    await using var f = await FuelRatePublicationFixture.CreateAsync();
    var previous = await f.SeedAsync();
    await f.Service.ReadAsync(default);
    var generation = f.Reads.Generation("fuel-exchange-rate");
    var calls = f.Publication.Calls;
    var updated = Current(.79m);
    f.Provider.Read = async _ =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      Assert.Equal(calls, f.Publication.Calls);
      Assert.Equal(previous, await f.Store.ReadAsync(default));
      return updated;
    };
    f.Publication.BeforeBegin = () =>
    {
      Assert.Equal(1, f.Provider.Calls);
      Assert.Equal(generation, f.Reads.Generation("fuel-exchange-rate"));
      return Task.CompletedTask;
    };

    Assert.True(await f.Service.RefreshAsync(default));

    Assert.Equal(calls + 1, f.Publication.Calls);
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(updated, await f.Store.ReadAsync(default));
    Assert.Equal(updated, await f.Service.ReadAsync(default));
    Assert.NotEqual(generation, f.Reads.Generation("fuel-exchange-rate"));
  }

  [Fact]
  public async Task ResultPublicationExcludesRateWritesUntilCommit()
  {
    await using var f = await FuelRatePublicationFixture.CreateAsync();
    var previous = await f.SeedAsync();
    await f.Store.AcquireAsync("rate-writer", DateTime.UtcNow, default);
    await using var writer = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(f.Connection.ConnectionString)
        .Options
    );
    var store = new FuelExchangeRateStore(
      writer,
      new PlanningPublicationScope(writer)
    );
    var updated = Current(.79m);

    await using (
      var transaction = await f.Publication.BeginAsync(null, default)
    )
    {
      Assert.Equal(previous, await f.Store.ReadAsync(default));
      var error = await Assert.ThrowsAsync<SqliteException>(
        () => store.SaveAsync("rate-writer", updated, default)
      );
      Assert.Contains(error.SqliteErrorCode, new[] { 5, 6 });
      Assert.Null(writer.Database.CurrentTransaction);
      Assert.Equal(previous, await f.Store.ReadAsync(default));
      await transaction.CommitAsync();
    }

    await store.SaveAsync("rate-writer", updated, default);
    Assert.Equal(updated, await f.Store.ReadAsync(default));
  }

  [Fact]
  public async Task RateWritesRejectAnOlderCallerTransaction()
  {
    await using var f = await FuelRatePublicationFixture.CreateAsync();
    var previous = await f.SeedAsync();
    await f.Store.AcquireAsync("writer", DateTime.UtcNow, default);
    await using var outer = await f.Db.Database.BeginTransactionAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Store.SaveAsync("writer", Current(.79m), default)
    );

    Assert.Same(outer, f.Db.Database.CurrentTransaction);
    Assert.Equal(previous, await f.Store.ReadAsync(default));
  }

  private static FuelExchangeRate Current(decimal rate) =>
    new(rate, DateOnly.FromDateTime(DateTime.UtcNow), DateTime.UtcNow);
}
