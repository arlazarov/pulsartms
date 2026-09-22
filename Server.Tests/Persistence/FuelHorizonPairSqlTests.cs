using Application.Features.Routing.Services.FuelPlanning;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// The pair filter is built by hand, so how its identifiers reach the database
// is a choice rather than something the compiler decides. Written as bare
// constants they were inlined into the SQL text, which gives every distinct
// set of loads its own statement and misses both EF's query cache and the
// server's plan cache. They are held and read as fields instead, the shape a
// captured local has, and this reads the generated SQL to say so.
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class FuelHorizonPairSqlTests
{
  private static FuelHorizon.SavedKey[] Keys(string first, string second) =>
    [
      new(
        Guid.Parse($"{first}-1111-4111-8111-111111111111"),
        Guid.Parse($"{first}-2222-4222-8222-222222222222")
      ),
      new(Guid.Parse($"{second}-3333-4333-8333-333333333333"), null),
    ];

  private static string Body(string sql) =>
    string.Join(
      "\n",
      sql.Split('\n').Where(line => !line.TrimStart().StartsWith("--"))
    );

  [RequiresPostgresFact]
  public async Task TheIdentifiersAreParametersAndTheStatementIsReusable()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var db = fixture.Connect();

    var one = Body(
      db.Set<DispatchBaseRoute>()
        .Where(
          FuelHorizon.ExactPairs<DispatchBaseRoute>(
            Keys("aaaaaaaa", "bbbbbbbb")
          )
        )
        .ToQueryString()
    );
    var two = Body(
      db.Set<DispatchBaseRoute>()
        .Where(
          FuelHorizon.ExactPairs<DispatchBaseRoute>(
            Keys("cccccccc", "dddddddd")
          )
        )
        .ToQueryString()
    );

    // No identifier appears in the statement itself.
    Assert.DoesNotContain("aaaaaaaa", one);
    Assert.DoesNotContain("bbbbbbbb", one);
    // They arrive as parameters, and the null leg still compares with IS NULL.
    Assert.Contains("@", one);
    Assert.Contains("IS NULL", one);
    // A different set of loads of the same size reuses the same statement.
    Assert.Equal(one, two);

    // Deadheads are filtered by the same builder and must behave the same.
    var deadheads = Body(
      db.Set<DispatchDeadhead>()
        .Where(
          FuelHorizon.ExactPairs<DispatchDeadhead>(Keys("aaaaaaaa", "bbbbbbbb"))
        )
        .ToQueryString()
    );
    Assert.DoesNotContain("aaaaaaaa", deadheads);
    Assert.Contains("IS NULL", deadheads);
  }
}
