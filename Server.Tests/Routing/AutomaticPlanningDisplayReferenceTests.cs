using System.Data.Common;
using Application.Caching;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

// The load's base road shown under a plan from the truck's position: one
// round trip per read, parsed once per row revision, and never an older
// road after the row changed - including a write that lands between the
// kept copy's lookup and the row read.
public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task ARepeatedPlanReadReusesTheParsedBaseRoad()
  {
    var reads = new BaseRoadReads();
    await using var f = await WithoutReferenceAsync(reads);
    var key = await ReferenceKeyAsync(f);
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var kept = f.Services.Displays.FindReference(key);
    Assert.NotNull(kept);
    reads.Count = 0;

    f.Db.ChangeTracker.Clear();
    await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.Equal(1, reads.Count);
    Assert.Same(kept, f.Services.Displays.FindReference(key));
  }

  [Fact]
  public async Task ABaseRoadWrittenAfterTheKeptCopyWasFoundIsTheOneShown()
  {
    var reads = new BaseRoadReads();
    await using var f = await WithoutReferenceAsync(reads);
    var key = await ReferenceKeyAsync(f);
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var before = f.Services.Displays.FindReference(key)!;
    var moved = new RoutePoint(40.5, -79.5);
    reads.Before = command => RewriteAsync(command, Road(moved));

    f.Db.ChangeTracker.Clear();
    var read = await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.Contains(
      moved,
      Assert.Single(read.State!.Plan!.ReferenceRoute!.Legs).Points
    );
    Assert.Equal(
      before.Revision + 1,
      f.Services.Displays.FindReference(key)!.Revision
    );
  }

  [Fact]
  public void AnOlderBaseRoadNeverReplacesANewerKeptOne()
  {
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions())
    );
    var displays = new RouteDisplayCache(reads);
    var key = (Guid.NewGuid(), (Guid?)null, 1);
    var company = Guid.NewGuid();
    var row = Guid.NewGuid();
    var newer = new RouteDisplayCache.DisplayReference(company, row, 2, null);

    displays.KeepReference(key, newer);
    displays.KeepReference(key, new(company, row, 1, null));

    Assert.Same(newer, displays.FindReference(key));
  }

  [Fact]
  public async Task EveryBaseRoadWriteRaisesItsRevision()
  {
    await using var f = await Fixture.CreateAsync(pickedUp: true);
    f.Location.UpdatedAt = DateTime.UtcNow.AddMinutes(-3);

    await f.Service.ForTruckAsync(f.Truck.Id, default);

    Assert.Equal(
      1,
      await f
        .Db.DispatchBaseRoutes.AsNoTracking()
        .Select(x => x.Revision)
        .SingleAsync()
    );
  }

  // A plan from the truck's position saved while its base road could not be
  // used as a reference: every read of it looks for the reference again.
  private static async Task<Fixture> WithoutReferenceAsync(BaseRoadReads reads)
  {
    var f = await Fixture.CreateAsync(
      recalculationBudgetEnabled: false,
      observer: reads
    );
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    await RewriteAsync(f.Db.Database.GetDbConnection(), "{\"legs\":[]}");
    f.Load.Stops[0].ManualCompletedAt = DateTime.UtcNow.AddMinutes(-10);
    f.Load.Stops[0].ManualCompletionRevision = 1;
    await f.Db.SaveChangesAsync();
    f.Services.Reads.Invalidate("dispatch");
    var built = await f.Service.ForTruckAsync(f.Truck.Id, default);
    Assert.True(built.State!.Plan!.FromCurrentPosition);
    f.Db.ChangeTracker.Clear();
    return f;
  }

  private static async Task<(Guid Load, Guid? Leg, int Legs)> ReferenceKeyAsync(
    Fixture f
  )
  {
    var row = await f.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync();
    return (row.DispatchId, row.ExecutionLegId, f.Load.Stops.Count - 1);
  }

  private static string Road(RoutePoint through)
  {
    var points = new List<RoutePoint> { new(40, -80), through, new(40, -79) };
    return RoutePlanStorage.Serialize(
      new TruckRoute
      {
        Miles = 110,
        Seconds = 7200,
        Legs = [new(110, 7200, points)],
        Points = points,
      }
    );
  }

  private static Task RewriteAsync(DbCommand read, string road) =>
    RewriteAsync(read.Connection!, road, read.Transaction);

  private static async Task RewriteAsync(
    DbConnection connection,
    string road,
    DbTransaction? transaction = null
  )
  {
    await using var write = connection.CreateCommand();
    write.Transaction = transaction;
    write.CommandText = """
      UPDATE "DispatchBaseRoutes"
      SET "RouteJson" = $road, "Revision" = "Revision" + 1
      """;
    var parameter = write.CreateParameter();
    parameter.ParameterName = "$road";
    parameter.Value = road;
    write.Parameters.Add(parameter);
    await write.ExecuteNonQueryAsync();
  }

  // Counts the display reference's row reads, the only base-road read that
  // chooses whether to bring the road's text; runs Before once ahead of the
  // next one.
  private sealed class BaseRoadReads : DbCommandInterceptor
  {
    public int Count;
    public Func<DbCommand, Task>? Before;

    public override async ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken ct = default
    )
    {
      if (
        command.CommandText.Contains("\"DispatchBaseRoutes\"")
        && command.CommandText.Contains("CASE")
      )
      {
        Count++;
        if (Before is { } before)
        {
          Before = null;
          await before(command);
        }
      }
      return result;
    }
  }
}
