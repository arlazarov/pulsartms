using System.Data.Common;
using Application.Behaviors;
using Application.Features.Routing.Interfaces;
using Application.Models;
using Domain.Entities;
using Domain.Entities.Fleet;
using Domain.Rules;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Server.Tests.Persistence;

// The truck's planning lock on PostgreSQL. Another pass holding it is
// ordinary contention: the answer is "being updated, retry shortly", with a
// retry time, and no database error; a truck with no revision row is a
// changed ownership. Once released, the lock is taken.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class PlanningPublicationScopePostgresTests
{
  [RequiresPostgresFact]
  public async Task AHeldTruckIsBusyWithARetryTimeAndThenFree()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var truck = await TruckAsync(fixture.Connect());
    await using var holder = fixture.Connect();
    await using var held = await holder.Database.BeginTransactionAsync();
    await holder
      .Database.SqlQueryRaw<int>(
        """
        SELECT 1 AS "Value" FROM "PlanningInputRevisions"
        WHERE "TruckId" = {0} FOR UPDATE
        """,
        truck
      )
      .ToListAsync();
    var failures = new FailedCommands();
    await using var db = fixture.Connect(failures);

    var busy = await Assert.ThrowsAsync<RoutePlanningException>(
      () => new PlanningPublicationScope(db).BeginAsync(truck, default)
    );

    Assert.Contains("being updated", busy.Message);
    Assert.True(busy.Busy);
    Assert.Equal(0, failures.Count);
    Assert.NotEqual(DateTime.MaxValue, busy.RetryAfter);
    await held.CommitAsync();
    await using var taken = await new PlanningPublicationScope(db).BeginAsync(
      truck,
      default
    );
    Assert.NotNull(taken);
  }

  // The same contention reaching a planning request's HTTP answer: a
  // conflict to retry (409) with the retry message, not a failure.
  [RequiresPostgresFact]
  public async Task AHeldTruckAnswersAPlanningRequestWithAConflict()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var truck = await TruckAsync(fixture.Connect());
    await using var holder = fixture.Connect();
    await using var held = await holder.Database.BeginTransactionAsync();
    await holder
      .Database.SqlQueryRaw<int>(
        """
        SELECT 1 AS "Value" FROM "PlanningInputRevisions"
        WHERE "TruckId" = {0} FOR UPDATE
        """,
        truck
      )
      .ToListAsync();
    var failures = new FailedCommands();
    await using var db = fixture.Connect(failures);

    var answer = await new PlanningExceptionBehavior<Publish, bool>().Handle(
      new Publish(),
      async ct =>
      {
        await using var opened = await new PlanningPublicationScope(
          db
        ).BeginAsync(truck, ct);
        return RequestResponse<bool>.Ok(true);
      },
      default
    );

    Assert.False(answer.Success);
    Assert.Equal(409, answer.StatusCode);
    Assert.Contains("being updated", Assert.Single(answer.Errors!));
    Assert.Equal(0, failures.Count);
  }

  private sealed record Publish
    : IRequest<RequestResponse<bool>>,
      IPlanningRequest;

  [RequiresPostgresFact]
  public async Task ATruckWithoutARevisionRowIsAChangedOwnership()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    await using var db = fixture.Connect();
    await RevisionAsync(db, Guid.Empty);

    var changed = await Assert.ThrowsAsync<RoutePlanningException>(
      () => new PlanningPublicationScope(db).BeginAsync(Guid.NewGuid(), default)
    );

    Assert.Contains("ownership changed", changed.Message);
  }

  private static async Task<Guid> TruckAsync(AppDbContext db)
  {
    await using (db)
    {
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        CompanyId = Company.Amf,
        ExternalId = "lock",
        UnitNumber = "LOCK-1",
        IsActive = true,
      };
      db.Trucks.Add(truck);
      await db.SaveChangesAsync();
      await RevisionAsync(db, truck.Id);
      return truck.Id;
    }
  }

  // The fixture's schema comes from the model, without the migrations' rows
  // and triggers: the global row and a truck's row are seeded here.
  private static async Task RevisionAsync(AppDbContext db, Guid truck)
  {
    await db.Database.ExecuteSqlRawAsync(
      """
      INSERT INTO "PlanningInputRevisions" ("TruckId", "Revision")
      VALUES ({0}, 0), ({1}, 0) ON CONFLICT DO NOTHING
      """,
      Guid.Empty,
      truck
    );
  }

  private sealed class FailedCommands : DbCommandInterceptor
  {
    public int Count;

    public override Task CommandFailedAsync(
      DbCommand command,
      CommandErrorEventData eventData,
      CancellationToken ct = default
    )
    {
      Count++;
      return Task.CompletedTask;
    }
  }
}
