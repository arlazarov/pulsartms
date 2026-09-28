using Application.Diagnostics.Consistency;
using Application.Features.Execution.Audit;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

// CW2 and CW3 of docs/architecture/current-work.md, from the rows as they
// stand: a load closed at its source whose accepted execution still plans
// or runs it (loads 1403 and 1385 on September 27), and a source change
// waiting for a dispatcher. Both are reviews; neither changes a row.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class SourceAheadAuditTests
{
  [Fact]
  public async Task AClosedSourceWithOpenExecutionIsReviewedOnly()
  {
    await using var f = await Fixture.CreateAsync();
    await ClosedSourceAsync(f);
  }

  [Fact]
  public async Task AnOpenSourceReviewIsReportedUntilItIsDecided()
  {
    await using var f = await Fixture.CreateAsync();
    await SourceReviewAsync(f);
  }

  internal static async Task ClosedSourceAsync(Fixture f)
  {
    var active = f.Linked("Completed", "active");
    var planned = f.Linked("completed", "planned");
    f.Linked("Completed", "completed");
    f.Linked("Completed", "cancelled");
    f.Linked("in_transit", "active");
    await f.Db.SaveChangesAsync();
    var rule = new SourceClosedWorkOpenRule(f.Db);

    var page = await rule.ReadAsync(f.Request(null, 1), default);
    var rest = await rule.ReadAsync(
      f.Request(page.Observed[^1].EntityKey, 10),
      default
    );
    var other = await rule.ReadAsync(
      f.Request(null, 10, Guid.NewGuid()),
      default
    );

    Assert.True(page.More);
    Assert.False(rest.More);
    Assert.Equal(
      new[] { active, planned }.Select(x => x.ToString()).Order(),
      page.Observed.Concat(rest.Observed).Select(x => x.EntityKey).Order()
    );
    Assert.Empty(other.Observed);
    Assert.Equal(ConsistencyCondition.Review, rule.Info.Condition);
    Assert.Equal(5, await f.Db.LoadExecutionLegs.CountAsync());
  }

  internal static async Task SourceReviewAsync(Fixture f)
  {
    var waiting = f.Sourced("assigned", "Review the initial assignment.");
    f.Sourced("assigned", null);
    f.Sourced("Completed", "Review the initial assignment.");
    f.Sourced("Canceled", "Review the initial assignment.");
    await f.Db.SaveChangesAsync();
    var rule = new SourceReviewOpenRule(f.Db);

    var page = await rule.ReadAsync(f.Request(null, 10), default);
    var finding = Assert.Single(page.Observed);
    await f
      .Db.DispatchSourceLinks.Where(x => x.DispatchId == waiting)
      .ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.ExecutionReviewReason, (string?)null)
      );
    var decided = await rule.ReadAsync(f.Request(null, 10), default);

    Assert.Equal(waiting.ToString(), finding.EntityKey);
    Assert.Equal("Review the initial assignment.", finding.Evidence["reason"]);
    Assert.Empty(decided.Observed);
  }

  internal sealed class Fixture : IAsyncDisposable
  {
    private readonly SqliteConnection? connection;
    private readonly Truck truck = new() { Id = Guid.NewGuid() };
    private int number = 1400;

    private Fixture(SqliteConnection? connection, AppDbContext db)
    {
      this.connection = connection;
      Db = db;
    }

    public AppDbContext Db { get; }

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
      return await ForAsync(connection, db);
    }

    // Over a database the caller made, such as the PostgreSQL fixture's.
    public static async Task<Fixture> ForAsync(
      SqliteConnection? connection,
      AppDbContext db
    )
    {
      var fixture = new Fixture(connection, db);
      db.Trucks.Add(fixture.truck);
      await db.SaveChangesAsync();
      return fixture;
    }

    public ConsistencyPageRequest Request(
      string? after,
      int limit,
      Guid? company = null
    ) =>
      new(
        company ?? Db.ServingCompany!.Value,
        DateTime.UtcNow,
        after,
        limit,
        TimeSpan.FromMinutes(30)
      );

    public Guid Linked(string source, string leg)
    {
      var load = Load(source);
      // One open leg per truck.
      var own = new Truck
      {
        Id = Guid.NewGuid(),
        UnitNumber = $"T{number}",
        ExternalId = $"T{number}",
      };
      Db.Trucks.Add(own);
      var link = new LoadExecutionLeg
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        ExecutionLeg = new ExecutionLeg
        {
          Id = Guid.NewGuid(),
          Trip = new() { Id = Guid.NewGuid() },
          TruckId = own.Id,
          Status = leg,
          Revision = 1,
        },
      };
      Db.LoadExecutionLegs.Add(link);
      return link.Id;
    }

    public Guid Sourced(string status, string? review)
    {
      var load = Load(status);
      Db.DispatchSourceLinks.Add(
        new DispatchSourceLink
        {
          Provider = "source",
          ExternalId = load.LoadNumber.ToString(),
          DispatchId = load.Id,
          Dispatch = load,
          ExecutionReviewReason = review,
        }
      );
      return load.Id;
    }

    private Load Load(string status)
    {
      var load = new Load
      {
        Id = Guid.NewGuid(),
        LoadNumber = ++number,
        Status = status,
        TruckId = truck.Id,
      };
      Db.Dispatches.Add(load);
      return load;
    }

    public async ValueTask DisposeAsync()
    {
      await Db.DisposeAsync();
      if (connection is not null)
        await connection.DisposeAsync();
    }
  }
}
