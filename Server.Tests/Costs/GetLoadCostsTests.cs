using Application.Features.Costs.Models;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Costs;

[Trait("Category", "Costs")]
[Trait("Kind", "Integration")]
public sealed class GetLoadCostsTests
{
  [Fact]
  public async Task ALoadBearsOnlyItsOwnShareOfAnExpense()
  {
    await using var f = await CostFixture.CreateAsync();
    await f.AttributeAsync([new(f.First, 250m), new(f.Second, 100m)], 0);

    var result = await f.CostsAsync(f.First);

    Assert.True(result.Success);
    var row = Assert.Single(result.Response!.Rows);
    Assert.Equal(250m, row.Amount);
    Assert.Equal(400m, row.ExpenseAmount);
    Assert.Equal("fuel", row.Kind);
  }

  [Fact]
  public async Task ALoadWithNoAttributedCostReportsNothingRatherThanFailing()
  {
    await using var f = await CostFixture.CreateAsync();

    var result = await f.CostsAsync(f.Second);

    Assert.True(result.Success);
    Assert.Empty(result.Response!.Rows);
    Assert.Empty(result.Response.Totals);
  }

  [Fact]
  public async Task TotalsAreKeptWithinOneCurrencyAndOneKind()
  {
    await using var f = await CostFixture.CreateAsync();
    await f.AttributeAsync([new(f.First, 250m)], 0);
    var toll = await f.AddExpenseAsync("toll", 60m, "USD");
    await f.AttributeAsync([new(f.First, 60m)], 0, toll);
    var canadian = await f.AddExpenseAsync("fuel", 80m, "CAD");
    await f.AttributeAsync([new(f.First, 80m)], 0, canadian);

    var totals = (await f.CostsAsync(f.First)).Response!.Totals;

    Assert.Equal(3, totals.Count);
    Assert.Equal(
      80m,
      totals.Single(x => x.Currency == "CAD" && x.Kind == "fuel").Amount
    );
    Assert.Equal(
      250m,
      totals.Single(x => x.Currency == "USD" && x.Kind == "fuel").Amount
    );
    Assert.Equal(
      60m,
      totals.Single(x => x.Currency == "USD" && x.Kind == "toll").Amount
    );
  }

  [Fact]
  public async Task AMissingLoadIsReportedRatherThanReturningAnEmptyReading()
  {
    await using var f = await CostFixture.CreateAsync();

    var result = await f.CostsAsync(Guid.NewGuid());

    Assert.False(result.Success);
    Assert.Equal(404, result.StatusCode);
  }

  [Fact]
  public async Task RemovingAShareRemovesTheCostFromThatLoad()
  {
    await using var f = await CostFixture.CreateAsync();
    await f.AttributeAsync([new(f.First, 250m), new(f.Second, 100m)], 0);

    await f.AttributeAsync([new(f.First, 250m)], 1);

    Assert.Empty((await f.CostsAsync(f.Second)).Response!.Rows);
    Assert.Single((await f.CostsAsync(f.First)).Response!.Rows);
    Assert.Equal(
      2,
      await f.Db.ExpenseAttributionEvents.CountAsync(x =>
        x.DispatchId == f.Second
      )
    );
  }
}
