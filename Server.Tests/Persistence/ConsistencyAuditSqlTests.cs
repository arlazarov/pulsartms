using Application.Diagnostics.Consistency;
using Application.Features.Execution.Audit;
using Application.Features.Routing.Audit;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Persistence;

// The audit reads page with Guid and string keyset comparisons that SQLite
// tests alone cannot vouch for on PostgreSQL. EF translates a query before
// it opens a connection, so against a port nothing listens on, a connection
// failure rather than a translation failure proves the SQL was produced.
// This is not a PostgreSQL execution test; that needs the isolated fixture.
// Journal writes open a transaction before their first query, so their SQL
// and the advisory lock are not covered here.
[Trait("Category", "Database")]
[Trait("Kind", "Unit")]
public sealed class ConsistencyAuditSqlTests
{
  private static AppDbContext Unreachable() =>
    new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(
          "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1"
        )
        .Options
    );

  public static TheoryData<string> Reads() =>
    [
      "execution.cancelled-source-runnable",
      "execution.cancelled-source-held",
      "execution.planning-change-overdue",
      "routing.planning-refresh-overdue",
      "messaging.unread-arrival-behind",
      "journal-events",
      "journal-incidents",
    ];

  [Theory]
  [MemberData(nameof(Reads))]
  public async Task EveryAuditReadTranslatesForPostgres(string read)
  {
    await using var db = Unreachable();
    var request = new ConsistencyPageRequest(
      Company.Amf,
      DateTime.UtcNow,
      read.StartsWith("routing") ? "cursor" : Guid.NewGuid().ToString(),
      10,
      TimeSpan.FromMinutes(30)
    );
    Func<Task> run = read switch
    {
      "execution.cancelled-source-runnable" => () =>
        new CancelledSourceRunnableRule(db).ReadAsync(request, default),
      "execution.cancelled-source-held" => () =>
        new CancelledSourceHeldRule(db).ReadAsync(request, default),
      "execution.planning-change-overdue" => () =>
        new ExecutionPlanningDemandRule(db).ReadAsync(request, default),
      "routing.planning-refresh-overdue" => () =>
        new PlanningRefreshDemandRule(new PlanningRefreshStore(db)).ReadAsync(
          request,
          default
        ),
      "messaging.unread-arrival-behind" => () =>
        new UnreadArrivalRule(db).ReadAsync(request, default),
      "journal-events" => () =>
        new ConsistencyJournalReads(db).EventsAsync(
          Company.Amf,
          5,
          10,
          "incident",
          default
        ),
      _ => () =>
        new ConsistencyJournalReads(db).IncidentsAsync(
          Company.Amf,
          Guid.NewGuid(),
          10,
          default
        ),
    };

    var failure = await Record.ExceptionAsync(run);

    Assert.NotNull(failure);
    Assert.DoesNotContain("could not be translated", failure.ToString());
    Assert.Contains("Npgsql.NpgsqlException", failure.ToString());
  }
}
