using Application.Features.Costs.Commands;
using Application.Features.Costs.Models;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Costs;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Costs;

[Trait("Category", "Costs")]
[Trait("Kind", "Integration")]
public sealed class SetExpenseAttributionTests
{
  [Fact]
  public async Task OneExpenseIsDividedBetweenTwoLoadsAndTheRestStaysVisible()
  {
    await using var f = await Fixture.CreateAsync();

    var result = await f.SetAsync(
      [new(f.First, 250m), new(f.Second, 100m)],
      revision: 0
    );

    Assert.True(result.Success);
    var row = result.Response!;
    Assert.Equal(50m, row.Unattributed);
    Assert.Equal(2, row.Shares.Count);
    Assert.Equal(1, row.Revision);
    Assert.Equal(2, await f.Db.ExpenseAttributionEvents.CountAsync());
  }

  [Fact]
  public async Task AnOverAttributedSetIsRefusedAndNothingIsWritten()
  {
    await using var f = await Fixture.CreateAsync();

    var result = await f.SetAsync(
      [new(f.First, 300m), new(f.Second, 200m)],
      revision: 0
    );

    Assert.False(result.Success);
    Assert.Equal(400, result.StatusCode);
    Assert.Empty(await f.Db.ExpenseAttributions.ToListAsync());
    Assert.Empty(await f.Db.ExpenseAttributionEvents.ToListAsync());
    Assert.Equal(0, (await f.Db.Expenses.SingleAsync()).Revision);
  }

  [Fact]
  public async Task AShareLeftOutIsRemovedAndItsHistoryRecordsZero()
  {
    await using var f = await Fixture.CreateAsync();
    await f.SetAsync([new(f.First, 250m), new(f.Second, 100m)], 0);

    var result = await f.SetAsync([new(f.First, 250m)], revision: 1);

    Assert.True(result.Success);
    Assert.Equal(f.First, Assert.Single(result.Response!.Shares).DispatchId);
    Assert.Equal(150m, result.Response.Unattributed);
    var removal = await f
      .Db.ExpenseAttributionEvents.Where(x => x.DispatchId == f.Second)
      .OrderBy(x => x.Revision)
      .LastAsync();
    Assert.Equal(0m, removal.Amount);
    Assert.Equal(100m, removal.PreviousAmount);
  }

  [Fact]
  public async Task AStaleRevisionIsRejectedAsAConflict()
  {
    await using var f = await Fixture.CreateAsync();
    await f.SetAsync([new(f.First, 100m)], 0);

    var result = await f.SetAsync([new(f.First, 200m)], revision: 0);

    Assert.Equal(409, result.StatusCode);
    Assert.Equal(100m, (await f.Db.ExpenseAttributions.SingleAsync()).Amount);
  }

  [Fact]
  public async Task AnUnchangedShareWritesNoNewHistory()
  {
    await using var f = await Fixture.CreateAsync();
    await f.SetAsync([new(f.First, 250m)], 0);

    var result = await f.SetAsync([new(f.First, 250m)], revision: 1);

    Assert.True(result.Success);
    Assert.Single(await f.Db.ExpenseAttributionEvents.ToListAsync());
  }

  [Fact]
  public async Task ClearingEveryShareLeavesTheWholeExpenseUnattributed()
  {
    await using var f = await Fixture.CreateAsync();
    await f.SetAsync([new(f.First, 250m)], 0);

    var result = await f.SetAsync([], revision: 1);

    Assert.True(result.Success);
    Assert.Empty(result.Response!.Shares);
    Assert.Equal(400m, result.Response.Unattributed);
    Assert.Empty(await f.Db.ExpenseAttributions.ToListAsync());
  }

  private sealed class Fixture : IAsyncDisposable
  {
    private SqliteConnection Connection { get; init; } = null!;
    public required AppDbContext Db { get; init; }
    public required Guid ExpenseId { get; init; }
    public required Guid First { get; init; }
    public required Guid Second { get; init; }
    public required SetExpenseAttributionHandler Handler { get; init; }

    public Task<Application.Models.RequestResponse<ExpenseAttributionRow>> SetAsync(
      ExpenseShareUpdate[] shares,
      long revision
    ) =>
      Handler.Handle(
        new(ExpenseId, new(revision, "manual", "Split by agreement", shares)),
        default
      );

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      var actor = new User
      {
        Id = Guid.NewGuid(),
        IdentityUserId = "identity",
        IsActive = true,
      };
      db.Users.Add(actor);
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "costs",
        UnitNumber = "1",
        IsActive = true,
      };
      db.Trucks.Add(truck);
      var loads = new[] { Load(truck, 1), Load(truck, 2) };
      db.AddRange(loads);
      var expense = new Expense
      {
        Id = Guid.NewGuid(),
        Kind = "fuel",
        Amount = 400m,
        Currency = "USD",
        OccurredAt = DateTime.UtcNow,
        RecordedAt = DateTime.UtcNow,
        RecordedBy = actor.Id,
      };
      db.Expenses.Add(expense);
      await db.SaveChangesAsync();
      return new Fixture
      {
        Connection = connection,
        Db = db,
        ExpenseId = expense.Id,
        First = loads[0].Id,
        Second = loads[1].Id,
        Handler = new(db, new Caller(), new Roles(), TimeProvider.System),
      };
    }

    private static DispatchEntity Load(Truck truck, int number) =>
      new()
      {
        Id = Guid.NewGuid(),
        Truck = truck,
        LoadNumber = number,
        Status = "assigned",
        ShipDate = DateOnly.FromDateTime(DateTime.UtcNow),
      };

    public async ValueTask DisposeAsync()
    {
      await Db.DisposeAsync();
      await Connection.DisposeAsync();
    }

    private sealed class Caller : ICurrentUser
    {
      public bool IsAuthenticated => true;
      public string? IdentityUserId => "identity";
    }

    private sealed class Roles : IUserRoleService
    {
      public Task<string?> GetAsync(
        string identityUserId,
        CancellationToken ct
      ) => Task.FromResult<string?>("Dispatch");

      public Task<Dictionary<Guid, string>> GetAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct
      ) => throw new NotSupportedException();

      public Task SetAsync(
        string identityUserId,
        string role,
        CancellationToken ct
      ) => throw new NotSupportedException();
    }
  }
}
