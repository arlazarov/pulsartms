using System.Globalization;
using System.Reflection;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Finance")]
[Trait("Kind", "Integration")]
public sealed class DeadheadHistoryReaderTests
{
  [Fact]
  public void PostgreSqlPredecessorLookupTranslatesToOneParameterizedLateralBatch()
  {
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=unused;Database=translation_only")
        .Options
    );
    var reader = new DeadheadHistoryReader(db);
    var method = typeof(DeadheadHistoryReader).GetMethod(
      "PostgresCandidates",
      BindingFlags.Instance | BindingFlags.NonPublic
    )!;
    var truck = Guid.NewGuid();
    var current = new[] { Load(truck, 2), Load(truck, 3) }
      .Select(RouteWorkProjection.Capture)
      .ToArray();
    var query = (IQueryable)method.Invoke(reader, [current])!;
    var sql = query.ToQueryString();
    Assert.Contains("JOIN LATERAL", sql);
    Assert.Contains("LIMIT 2", sql);
    Assert.Contains("unnest(@", sql);
    Assert.Contains(truck.ToString(), sql);
    Assert.Contains(current[0].Id.ToString(), sql);
    Assert.Contains(
      current[0]
        .Stops[0]
        .ScheduledDate!.Value.ToString(CultureInfo.InvariantCulture),
      sql
    );
    Assert.Contains("ORDER BY", sql);
    Assert.Contains("cancelled", sql);
  }

  [Fact]
  public async Task CandidateMaterializationIsBoundedAndRetainsUnknownAndTiedHistory()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck { Id = Guid.NewGuid() };
    db.Trucks.Add(truck);
    var history = Enumerable
      .Range(0, 100)
      .Select(i => Load(truck.Id, i))
      .ToList();
    db.Dispatches.AddRange(history);
    await db.SaveChangesAsync();
    var reader = new DeadheadHistoryReader(db);
    var current = history[^1];
    var snapshot = (await reader.ReadAsync([current.Id], default))[current.Id];
    Assert.Equal(2, snapshot.Predecessors.Length);
    Assert.Equal(
      DeadheadConnection.Find(current, history)!.Previous.Id,
      DeadheadConnection
        .Find(DeadheadHistoryProjection.Capture(snapshot))!
        .Previous.Id
    );
    var page = await reader.ReadAsync(
      history.TakeLast(10).Select(x => x.Id).ToArray(),
      default
    );
    Assert.Equal(10, page.Count);
    Assert.All(
      page.Values,
      value =>
      {
        Assert.Equal(2, value.Predecessors.Length);
        Assert.Equal(
          DeadheadConnection
            .Find(value.Current, history.Select(RouteWorkProjection.Capture))!
            .Previous.Id,
          DeadheadConnection
            .Find(DeadheadHistoryProjection.Capture(value))!
            .Previous.Id
        );
      }
    );
    var duplicate = Load(truck.Id, 100);
    duplicate.Stops[0].ScheduledDate = history[^2].Stops[0].ScheduledDate;
    db.Dispatches.Add(duplicate);
    await db.SaveChangesAsync();
    snapshot = (await reader.ReadAsync([current.Id], default))[current.Id];
    Assert.Equal(2, snapshot.Predecessors.Length);
    Assert.Null(
      DeadheadConnection.Find(DeadheadHistoryProjection.Capture(snapshot))
    );
    duplicate.Status = "cancelled";
    history[0].Stops[0].ScheduledDate = null;
    await db.SaveChangesAsync();
    snapshot = (await reader.ReadAsync([current.Id], default))[current.Id];
    Assert.True(snapshot.HasUnknownStart);
    Assert.Empty(snapshot.Predecessors);
    Assert.Null(
      DeadheadConnection.Find(DeadheadHistoryProjection.Capture(snapshot))
    );
    history[0].Status = "cancelled";
    await db.SaveChangesAsync();
    snapshot = (await reader.ReadAsync([current.Id], default))[current.Id];
    Assert.False(snapshot.HasUnknownStart);
    Assert.Equal(
      history[^2].Id,
      DeadheadConnection
        .Find(DeadheadHistoryProjection.Capture(snapshot))!
        .Previous.Id
    );
  }

  private static Dispatch Load(Guid truck, int index)
  {
    var date = new DateOnly(2026, 1, 1).AddDays(index * 2);
    return new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = index,
      TruckId = truck,
      Status = "assigned",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          ScheduledDate = date,
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Job = "Drop Off",
          ScheduledDate = date.AddDays(1),
        },
      ],
    };
  }
}
