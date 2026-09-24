using System.Data.Common;
using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Routing;

// A plan from the truck's position saved before its load's base road could
// be used looked for the road on every read and never kept it. The planning
// pass, the plan's own writer, now gives it the road as its display
// reference once. The reference is display: the plan's version (fuel and
// ETA follow it), the driven road's revision (movement and history follow
// it) and the history stay as they were. A road that changes under the
// pass is not attached, and a plan replaced meanwhile is not overwritten.
public partial class AutomaticPlanningTests
{
  [Fact]
  public async Task APlanSavedBeforeItsBaseRoadGetsItAsReferenceOnce()
  {
    var reads = new BaseRoadReads();
    await using var f = await WithoutReferenceAsync(reads);
    var before = await StoredAsync(f);
    Assert.Null(before.Plan.ReferenceStops);
    await RewriteAsync(f.Db.Database.GetDbConnection(), Road(Detour));
    reads.Count = 0;
    f.Db.ChangeTracker.Clear();
    var readTime = await f.Reader.ForTruckAsync(f.Truck.Id, default);
    Assert.True(reads.Count > 0, "a plan read looks for the base road");
    var source = readTime.State!.Plan!.ReferenceSource;
    Assert.NotNull(source);

    f.Db.ChangeTracker.Clear();
    await f.Service.ForTruckAsync(f.Truck.Id, default);

    var after = await StoredAsync(f);
    Assert.NotNull(after.Plan.ReferenceStops);
    Assert.Contains(
      Detour,
      Assert.Single(after.Plan.ReferenceRoute!.Legs).Points
    );
    Assert.Equal(before.Plan.Version, after.Plan.Version);
    Assert.Equal(before.Revision, after.Revision);
    Assert.Equal(before.History, after.History);
    Assert.Equal(before.Plan.Route.Miles, after.Plan.Route.Miles);
    reads.Count = 0;
    f.Db.ChangeTracker.Clear();
    var shown = await f.Reader.ForTruckAsync(f.Truck.Id, default);
    Assert.Equal(0, reads.Count);
    Assert.Contains(
      Detour,
      Assert.Single(shown.State!.Plan!.ReferenceRoute!.Legs).Points
    );
    // The same base road, read or stored: an open map has nothing new to
    // fetch when the reference is stored.
    Assert.Equal(source, after.Plan.ReferenceSource);
    Assert.Equal(source, shown.State.Plan.ReferenceSource);
  }

  // Within one version a stored reference never changes: a reference that
  // differs from the stored one - here the same legs and miles through other
  // points - is saved as a new version like any other change.
  [Fact]
  public async Task AStoredReferenceThatChangesIsANewVersion()
  {
    await using var f = await WithoutReferenceAsync(new BaseRoadReads());
    await RewriteAsync(f.Db.Database.GetDbConnection(), Road(Detour));
    f.Db.ChangeTracker.Clear();
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var attached = await StoredAsync(f);
    Assert.NotNull(attached.Plan.ReferenceStops);

    f.Db.ChangeTracker.Clear();
    var entity = (await f.Services.RoutePlans.ReadAsync(f.Load.Id, default))!;
    var plan = RoutePlanStorage.Read(entity)!;
    var leg = Assert.Single(plan.ReferenceRoute!.Legs);
    plan.ReferenceRoute.Legs =
    [
      leg with
      {
        Points = [new(40, -80), new(40.3, -79.6), new(40, -79)],
      },
    ];
    await f.Services.RoutePlans.SaveAsync(entity, plan, default);

    var changed = await StoredAsync(f);
    Assert.Equal(attached.Plan.Version + 1, changed.Plan.Version);
    Assert.Equal(attached.Revision + 1, changed.Revision);
    Assert.Equal(attached.History + 1, changed.History);
  }

  [Fact]
  public async Task ABaseRoadRewrittenDuringThePassIsNotAttached()
  {
    var reads = new BaseRoadReads();
    await using var f = await WithoutReferenceAsync(reads);
    await RewriteAsync(f.Db.Database.GetDbConnection(), Road(Detour));
    var later = new RoutePoint(40.3, -79.6);
    reads.After = connection =>
      RewriteAsync(
        connection,
        Road(later),
        calculatedAt: DateTime.UtcNow.AddMinutes(1)
      );

    // The pass itself, so its read of the road is the one the write follows.
    f.Db.ChangeTracker.Clear();
    await f.Plans.AdvanceAutomaticallyAsync(f.Load.Id, default);

    Assert.Null((await StoredAsync(f)).Plan.ReferenceStops);
    f.Db.ChangeTracker.Clear();
    await f.Service.ForTruckAsync(f.Truck.Id, default);
    var attached = (await StoredAsync(f)).Plan;
    Assert.Contains(later, Assert.Single(attached.ReferenceRoute!.Legs).Points);
  }

  [Fact]
  public async Task APlanReplacedDuringThePassIsNotOverwritten()
  {
    var reads = new BaseRoadReads();
    await using var f = await WithoutReferenceAsync(reads);
    await RewriteAsync(f.Db.Database.GetDbConnection(), Road(Detour));
    string? replacement = null;
    reads.After = async connection =>
    {
      await using var write = connection.CreateCommand();
      write.CommandText = """
        UPDATE "DispatchRoutePlans"
        SET "PlanJson" = replace("PlanJson", '"version":', '"version":9')
        RETURNING "PlanJson"
        """;
      replacement = (string?)await write.ExecuteScalarAsync();
    };

    f.Db.ChangeTracker.Clear();
    await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
      () => f.Plans.AdvanceAutomaticallyAsync(f.Load.Id, default)
    );

    Assert.NotNull(replacement);
    Assert.Equal(
      replacement,
      await f
        .Db.DispatchRoutePlans.AsNoTracking()
        .Select(x => x.PlanJson)
        .SingleAsync()
    );
  }

  private static readonly RoutePoint Detour = new(40.5, -79.5);

  private sealed record Stored(RoutePlan Plan, long Revision, int History);

  private static async Task<Stored> StoredAsync(Fixture f)
  {
    f.Db.ChangeTracker.Clear();
    var entity = await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync();
    await RoutePlanStorage.LoadAsync(f.Db, entity, default);
    return new(
      RoutePlanStorage.Read(entity)!,
      entity.GeometryRevision,
      await f.Db.RouteGeometryChanges.CountAsync()
    );
  }

  // A plan from the truck's position built while the load's base road was
  // incomplete: saved with no reference, as the background pass leaves one
  // built before the road is written.
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
    Assert.Null((await StoredAsync(f)).Plan.ReferenceStops);
    return f;
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

  private static async Task RewriteAsync(
    DbConnection connection,
    string road,
    DbTransaction? transaction = null,
    DateTime? calculatedAt = null
  )
  {
    await using var write = connection.CreateCommand();
    write.Transaction = transaction;
    write.CommandText = """
      UPDATE "DispatchBaseRoutes"
      SET "RouteJson" = $road, "CalculatedAt" = $at
      """;
    var parameter = write.CreateParameter();
    parameter.ParameterName = "$road";
    parameter.Value = road;
    write.Parameters.Add(parameter);
    var at = write.CreateParameter();
    at.ParameterName = "$at";
    at.Value = calculatedAt ?? DateTime.UtcNow;
    write.Parameters.Add(at);
    await write.ExecuteNonQueryAsync();
  }

  // Counts reads of a base road's text, the reads a display reference
  // costs. After runs once, as a committed write of its own, when the next
  // transaction starts: after the pass has read the road, before it checks
  // the road again under the publication lock and commits.
  private sealed class BaseRoadReads
    : DbCommandInterceptor,
      IDbTransactionInterceptor
  {
    public int Count;
    public Func<DbConnection, Task>? After;
    private bool armed;

    public async ValueTask<
      InterceptionResult<DbTransaction>
    > TransactionStartingAsync(
      DbConnection connection,
      TransactionStartingEventData eventData,
      InterceptionResult<DbTransaction> result,
      CancellationToken ct = default
    )
    {
      if (armed && After is { } after)
      {
        armed = false;
        After = null;
        await after(connection);
      }
      return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
      DbCommand command,
      CommandExecutedEventData eventData,
      DbDataReader result,
      CancellationToken ct = default
    )
    {
      if (
        command.CommandText.Contains("FROM \"DispatchBaseRoutes\"")
        && command.CommandText.Contains("\"RouteJson\"")
      )
      {
        Count++;
        armed = After is not null;
      }
      return ValueTask.FromResult(result);
    }
  }
}
