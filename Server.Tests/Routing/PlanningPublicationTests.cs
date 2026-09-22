using System.Data;
using Application.Caching;
using Application.Features.Execution.Services;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Application.Reference;
using Domain.Entities.Fleet;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class PlanningPublicationTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SummaryChangesOnlyAfterCommitAndReadInvalidation(
    bool commit
  )
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var company = new TestCompany();
    var cache = new PlanningSummaryCache(TimeProvider.System);
    var key = new PlanningSummaryCache.Key(company.Id!.Value, f.Truck.Id);
    cache.Read(key, "work");
    var captured = cache.Take()!;
    var publication = new PlanningWorkPublication(
      f.Planning.Itineraries,
      new PublicationProbe(f.Db),
      f.Planning.DeadheadHistory,
      cache,
      company,
      f.Planning.Reads
    );
    await using var transaction = await f.Db.Database.BeginTransactionAsync();
    if (commit)
    {
      await publication.CommitAsync(
        transaction,
        f.Truck.Id,
        default,
        () => Assert.True(cache.IsCurrent(captured))
      );
      Assert.False(cache.IsCurrent(captured));
      Assert.True(cache.IsCurrent(publication.Current(captured)));
    }
    else
    {
      await transaction.RollbackAsync();
      await Assert.ThrowsAnyAsync<InvalidOperationException>(
        () => publication.CommitAsync(transaction, f.Truck.Id, default)
      );
      Assert.True(cache.IsCurrent(captured));
    }
  }

  [Fact]
  public async Task UnsupportedProvidersCannotSilentlyPublishWithoutProtection()
  {
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase("publication-" + Guid.NewGuid())
        .Options
    );
    await Assert.ThrowsAsync<NotSupportedException>(
      () => new PlanningPublicationScope(db).BeginAsync(null, default)
    );
  }

  [Fact]
  public async Task ValidationAndWritesShareOneOwnedTransaction()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var work = (
      await f.Planning.PlanningInputs.ReadFreshAsync(f.Truck.Id, default)
    )!.Itinerary;

    await using (
      var transaction = await f.Planning.Publication.BeginAsync(work, default)
    )
    {
      Assert.Same(transaction, f.Db.Database.CurrentTransaction);
      Assert.Equal(
        IsolationLevel.Serializable,
        transaction.GetDbTransaction().IsolationLevel
      );
      var current = await f.Planning.Itineraries.ReadAsync(
        work.TruckId,
        work.AsOf,
        default
      );
      Assert.Equal(work.InputSignature, current!.InputSignature);
      f.Db.TruckPlanningProfiles.Add(
        new()
        {
          Id = Guid.NewGuid(),
          TruckId = work.TruckId,
          SettingsJson = "{}",
        }
      );
      await f.Db.SaveChangesAsync();
      await transaction.CommitAsync();
    }

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Single(
      await f.Db.TruckPlanningProfiles.AsNoTracking().ToListAsync()
    );
  }

  [Fact]
  public async Task ChangedWorkDisposesTheTransactionBeforeAnyResultWrite()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var work = (
      await f.Planning.PlanningInputs.ReadFreshAsync(f.Truck.Id, default)
    )!.Itinerary;
    var probe = new PublicationProbe(f.Db)
    {
      BeforeBegin = async () =>
        await f
          .Db.DispatchStops.Where(x => x.Id == f.Load.Stops[0].Id)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Notes, "new facts")),
    };
    var publication = new PlanningWorkPublication(
      f.Planning.Itineraries,
      probe,
      f.Planning.DeadheadHistory,
      new PlanningSummaryCache(TimeProvider.System),
      new TestCompany(),
      f.Planning.Reads
    );

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => publication.BeginAsync(work, default)
    );

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Empty(await f.Db.DispatchRoutePlans.ToListAsync());
    Assert.Equal(1, probe.Calls);
    var fresh = (
      await f.Planning.PlanningInputs.ReadFreshAsync(f.Truck.Id, default)
    )!.Itinerary;
    probe.BeforeBegin = null;
    await using var retry = await publication.BeginAsync(fresh, default);
    await retry.CommitAsync();
  }

  [Fact]
  public async Task DisposalRollsBackEveryAlreadyWrittenPart()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    var work = (
      await f.Planning.PlanningInputs.ReadFreshAsync(f.Truck.Id, default)
    )!.Itinerary;

    await using (await f.Planning.Publication.BeginAsync(work, default))
    {
      f.Db.TruckPlanningProfiles.Add(
        new()
        {
          Id = Guid.NewGuid(),
          TruckId = work.TruckId,
          SettingsJson = "{}",
        }
      );
      await f.Db.SaveChangesAsync();
      f.Db.DispatchRoutePlans.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Load.Id,
          TruckId = work.TruckId,
          InputHash = "rollback",
          PlanJson = "{}",
        }
      );
      await f.Db.SaveChangesAsync();
    }

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Empty(await f.Db.TruckPlanningProfiles.AsNoTracking().ToListAsync());
    Assert.Empty(await f.Db.DispatchRoutePlans.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task OuterTransactionIsRejectedWithoutTakingItsOwnership()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    await using var outer = await f.Db.Database.BeginTransactionAsync();

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => new PlanningPublicationScope(f.Db).BeginAsync(null, default)
    );

    Assert.Same(outer, f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task CancellationBeforeEntryDoesNotOpenATransaction()
  {
    await using var f = await RouteChoiceFixture.CreateAsync();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        new PlanningPublicationScope(f.Db).BeginAsync(null, cancellation.Token)
    );

    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public async Task SQLiteProtectsStoredSettingsIncludingMissingDefaults(
    bool fleet,
    bool insert
  )
  {
    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = "planning-settings-" + Guid.NewGuid(),
      Mode = SqliteOpenMode.Memory,
      Cache = SqliteCacheMode.Shared,
      DefaultTimeout = 1,
    }.ToString();
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    var profile = new TruckPlanningProfile
    {
      Id = Guid.NewGuid(),
      TruckId = truck.Id,
      SettingsJson = "{}",
    };
    var settings = new FleetPlanningSettings
    {
      Id = Guid.NewGuid(),
      SettingsJson = "{}",
    };
    if (!insert)
    {
      if (fleet)
        db.FleetPlanningSettings.Add(settings);
      else
        db.TruckPlanningProfiles.Add(profile);
      await db.SaveChangesAsync();
      db.ChangeTracker.Clear();
    }
    await using var writer = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .Options
    );
    if (insert)
    {
      if (fleet)
        writer.FleetPlanningSettings.Add(settings);
      else
        writer.TruckPlanningProfiles.Add(profile);
    }
    async Task Write()
    {
      if (insert)
        await writer.SaveChangesAsync();
      else if (fleet)
        await writer.FleetPlanningSettings.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.SettingsJson, "updated")
        );
      else
        await writer.TruckPlanningProfiles.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.SettingsJson, "updated")
        );
    }
    Task<string?> Read() =>
      fleet
        ? db
          .FleetPlanningSettings.Select(x => x.SettingsJson)
          .SingleOrDefaultAsync()
        : db
          .TruckPlanningProfiles.Select(x => x.SettingsJson)
          .SingleOrDefaultAsync();

    await using (
      var transaction = await new PlanningPublicationScope(db).BeginAsync(
        null,
        default
      )
    )
    {
      var expected = insert ? null : "{}";
      Assert.Equal(expected, await Read());
      var error = insert
        ? Assert.IsType<SqliteException>(
          (await Assert.ThrowsAsync<DbUpdateException>(Write)).InnerException
        )
        : await Assert.ThrowsAsync<SqliteException>(Write);
      Assert.Contains(error.SqliteErrorCode, new[] { 5, 6 });
      Assert.Equal(expected, await Read());
      await transaction.CommitAsync();
    }

    await Write();
    Assert.Equal(insert ? "{}" : "updated", await Read());
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SQLiteProtectsExistingRowsAndNewMembershipUntilDisposal(
    bool insert
  )
  {
    var connectionString = new SqliteConnectionStringBuilder
    {
      DataSource = "publication-" + Guid.NewGuid(),
      Mode = SqliteOpenMode.Memory,
      Cache = SqliteCacheMode.Shared,
      DefaultTimeout = 1,
    }.ToString();
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid(), ExternalId = "publication" };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    var reader = new TruckItineraryReader(
      db,
      new ExecutionReadScope(db),
      new FleetNames(db),
      new ActiveTransfers(db)
    );
    var work = (
      await reader.ReadAsync(truck.Id, DateTimeOffset.UtcNow, default)
    )!;
    var publication = new PlanningWorkPublication(
      reader,
      new PlanningPublicationScope(db),
      new(
        db,
        new DeadheadHistoryReader(db),
        new ExecutionReadScope(db),
        new FleetNames(db),
        new ActiveTransfers(db)
      ),
      new PlanningSummaryCache(TimeProvider.System),
      new TestCompany(),
      new ReadCache(Options.Create(new SynchronizationOptions()))
    );
    await using var writer = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connectionString)
        .Options
    );
    if (insert)
      writer.Dispatches.Add(
        new()
        {
          Id = Guid.NewGuid(),
          TruckId = truck.Id,
          LoadNumber = 55,
          Status = "assigned",
        }
      );
    async Task Write()
    {
      if (insert)
        await writer.SaveChangesAsync();
      else
        await writer
          .Trucks.Where(x => x.Id == truck.Id)
          .ExecuteUpdateAsync(s =>
            s.SetProperty(x => x.ConfigurationRevision, 1)
          );
    }

    await using (var transaction = await publication.BeginAsync(work, default))
    {
      var error = insert
        ? Assert.IsType<SqliteException>(
          (await Assert.ThrowsAsync<DbUpdateException>(Write)).InnerException
        )
        : await Assert.ThrowsAsync<SqliteException>(Write);
      Assert.Contains(error.SqliteErrorCode, new[] { 5, 6 });
      Assert.Equal(
        work.InputSignature,
        (await reader.ReadAsync(truck.Id, work.AsOf, default))!.InputSignature
      );
      await transaction.CommitAsync();
    }
    await Write();
    Assert.NotEqual(
      work.InputSignature,
      (await reader.ReadAsync(truck.Id, work.AsOf, default))!.InputSignature
    );
  }
}
