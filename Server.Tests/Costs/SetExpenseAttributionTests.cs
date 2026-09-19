using Application.Features.Costs.Models;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Costs;

[Trait("Category", "Costs")]
[Trait("Kind", "Integration")]
public sealed class SetExpenseAttributionTests
{
  [Fact]
  public async Task OneExpenseIsDividedBetweenTwoLoadsAndTheRestStaysVisible()
  {
    await using var f = await CostFixture.CreateAsync();

    var result = await f.AttributeAsync(
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
    await using var f = await CostFixture.CreateAsync();

    var result = await f.AttributeAsync(
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
    await using var f = await CostFixture.CreateAsync();
    await f.AttributeAsync([new(f.First, 250m), new(f.Second, 100m)], 0);

    var result = await f.AttributeAsync([new(f.First, 250m)], revision: 1);

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
    await using var f = await CostFixture.CreateAsync();
    await f.AttributeAsync([new(f.First, 100m)], 0);

    var result = await f.AttributeAsync([new(f.First, 200m)], revision: 0);

    Assert.Equal(409, result.StatusCode);
    Assert.Equal(100m, (await f.Db.ExpenseAttributions.SingleAsync()).Amount);
  }

  [Fact]
  public async Task AnUnchangedShareWritesNoNewHistory()
  {
    await using var f = await CostFixture.CreateAsync();
    await f.AttributeAsync([new(f.First, 250m)], 0);

    var result = await f.AttributeAsync([new(f.First, 250m)], revision: 1);

    Assert.True(result.Success);
    Assert.Single(await f.Db.ExpenseAttributionEvents.ToListAsync());
  }

  [Fact]
  public async Task ClearingEveryShareLeavesTheWholeExpenseUnattributed()
  {
    await using var f = await CostFixture.CreateAsync();
    await f.AttributeAsync([new(f.First, 250m)], 0);

    var result = await f.AttributeAsync([], revision: 1);

    Assert.True(result.Success);
    Assert.Empty(result.Response!.Shares);
    Assert.Equal(400m, result.Response.Unattributed);
    Assert.Empty(await f.Db.ExpenseAttributions.ToListAsync());
  }
}
