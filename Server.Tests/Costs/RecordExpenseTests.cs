using Application.Features.Costs.Models;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Costs;

[Trait("Category", "Costs")]
[Trait("Kind", "Integration")]
public sealed class RecordExpenseTests
{
  private static ExpenseEntry Entry(
    decimal amount = 120m,
    string kind = "toll",
    string currency = "usd"
  ) => new(kind, DateTime.UtcNow, amount, currency);

  [Fact]
  public async Task ARecordedExpenseStartsWhollyUnattributed()
  {
    await using var f = await CostFixture.CreateAsync();

    var result = await f.RecordAsync(Entry());

    Assert.True(result.Success);
    Assert.Equal(120m, result.Response!.Unattributed);
    Assert.Empty(result.Response.Shares);
    Assert.Equal("USD", result.Response.Currency);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-5)]
  public async Task AnExpenseNeedsAnAmountAboveZero(decimal amount)
  {
    await using var f = await CostFixture.CreateAsync();

    var result = await f.RecordAsync(Entry(amount));

    Assert.Equal(400, result.StatusCode);
    Assert.Empty(
      await f.Db.Expenses.Where(x => x.Kind == "toll").ToListAsync()
    );
  }

  [Fact]
  public async Task AnUnrecordedKindIsRefused()
  {
    await using var f = await CostFixture.CreateAsync();

    var result = await f.RecordAsync(Entry(kind: "settlement"));

    Assert.Equal(400, result.StatusCode);
  }

  [Fact]
  public async Task AnImportedExpenseIsRecordedOnceHoweverOftenItIsReplayed()
  {
    await using var f = await CostFixture.CreateAsync();
    var key = Guid.NewGuid();
    var entry = Entry() with { IdempotencyKey = key, Source = "bvd" };

    Assert.True((await f.RecordAsync(entry)).Success);
    var replay = await f.RecordAsync(entry);

    Assert.Equal(409, replay.StatusCode);
    Assert.Single(
      await f.Db.Expenses.Where(x => x.IdempotencyKey == key).ToListAsync()
    );
  }

  [Fact]
  public async Task SeveralHandEnteredExpensesCanExistWithoutAKey()
  {
    await using var f = await CostFixture.CreateAsync();

    Assert.True((await f.RecordAsync(Entry())).Success);
    Assert.True((await f.RecordAsync(Entry(80m))).Success);

    Assert.Equal(
      2,
      await f.Db.Expenses.CountAsync(x =>
        x.IdempotencyKey == null && x.Kind == "toll"
      )
    );
  }

  [Fact]
  public async Task TheSourceNamesAreKeptExactlyAsSupplied()
  {
    await using var f = await CostFixture.CreateAsync();

    await f.RecordAsync(
      Entry() with
      {
        SourceTruckName = "  11007  ",
        SourceDriverName = "  MARIO R.  ",
      }
    );

    var saved = await f.Db.Expenses.SingleAsync(x => x.Kind == "toll");
    Assert.Equal("11007", saved.SourceTruckName);
    Assert.Equal("MARIO R.", saved.SourceDriverName);
  }
}
