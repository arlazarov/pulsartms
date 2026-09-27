using Domain.Entities.Costs;
using Server.Tests.Support;

namespace Server.Tests.Persistence;

// F22's totals past the page are summed in the database, grouped by
// currency and kind; SQLite accepting that says nothing about PostgreSQL.
// The shares are written as rows in one save: this checks the reader's
// SQL, and 210 attribution commands against the remote fixture took 12
// minutes (the SQLite tests go through the command).
[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class LoadCostsPostgresTests
{
  [RequiresPostgresFact]
  public async Task TotalsPastThePageKeepEachCurrencyAndKind()
  {
    await using var postgres = await PostgresFixture.CreateAsync();
    await using var f = await CostFixture.ForAsync(postgres.Connect());
    Shares(f, 150, "toll", "USD");
    Shares(f, 60, "fuel", "CAD");
    await f.Db.SaveChangesAsync();

    var result = (await f.CostsAsync(f.First)).Response!;

    Assert.True(result.Truncated);
    Assert.Equal(200, result.Rows.Count);
    Assert.Equal(
      [("CAD", "fuel", 60m), ("USD", "toll", 150m)],
      result.Totals.Select(x => (x.Currency, x.Kind, x.Amount)).ToArray()
    );
  }

  private static void Shares(
    CostFixture f,
    int count,
    string kind,
    string currency
  )
  {
    for (var i = 0; i < count; i++)
    {
      var expense = CostFixture.NewExpense(kind, 1m, currency, f.Actor);
      f.Db.Expenses.Add(expense);
      f.Db.Set<ExpenseAttribution>()
        .Add(
          new()
          {
            Id = Guid.NewGuid(),
            ExpenseId = expense.Id,
            DispatchId = f.First,
            Amount = 1m,
            Revision = 1,
            RecordedAt = DateTime.UtcNow,
            RecordedBy = f.Actor,
          }
        );
    }
  }
}
