using System.Data.Common;
using Application.Features.Costs.Models;
using Application.Interfaces;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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

  // The rows shown stop at a page; the totals do not (audit F22). A load
  // with more shares than the page used to total the page alone.
  [Fact]
  public async Task TotalsCoverEveryShareWhenTheRowsStopAtAPage()
  {
    await using var f = await CostFixture.CreateAsync();
    for (var i = 0; i < 201; i++)
    {
      var toll = await f.AddExpenseAsync("toll", 1m, "USD");
      await f.AttributeAsync([new(f.First, 1m)], 0, toll);
    }

    var result = (await f.CostsAsync(f.First)).Response!;

    Assert.True(result.Truncated);
    Assert.Equal(200, result.Rows.Count);
    Assert.Equal(201m, Assert.Single(result.Totals).Amount);
  }

  // Past the page the totals are summed in the database, per currency and
  // kind, and only the serving carrier's shares count there too.
  [Fact]
  public async Task TotalsPastThePageKeepEachCurrencyAndKind()
  {
    await using var f = await CostFixture.CreateAsync();
    await AddSharesAsync(f, 150, "toll", "USD");
    await AddSharesAsync(f, 60, "fuel", "CAD");

    var result = (await f.CostsAsync(f.First)).Response!;

    Assert.True(result.Truncated);
    Assert.Equal(200, result.Rows.Count);
    Assert.Equal(
      [("CAD", "fuel", 60m), ("USD", "toll", 150m)],
      result.Totals.Select(x => (x.Currency, x.Kind, x.Amount)).ToArray()
    );
  }

  [Fact]
  public async Task AnotherCarriersSharesNeverReachTheTotals()
  {
    await using var f = await CostFixture.CreateAsync();
    await AddSharesAsync(f, 201, "toll", "USD");
    var other = Guid.NewGuid();
    var company = new TestCompany();
    var services = new ServiceCollection()
      .AddSingleton<ICurrentCompany>(company)
      .BuildServiceProvider();
    await using (
      var theirs = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(f.Shared)
          .UseApplicationServiceProvider(services)
          .Options
      )
    )
    using (company.As(other))
    {
      theirs.Companies.Add(
        new()
        {
          Id = other,
          Key = "other",
          Name = "Other",
        }
      );
      for (var i = 0; i < 5; i++)
      {
        var expense = CostFixture.NewExpense("toll", 7m, "USD", f.Actor);
        theirs.Expenses.Add(expense);
        theirs.ExpenseAttributions.Add(
          new()
          {
            Id = Guid.NewGuid(),
            ExpenseId = expense.Id,
            DispatchId = f.First,
            Amount = 7m,
          }
        );
      }
      await theirs.SaveChangesAsync();
    }

    var result = (await f.CostsAsync(f.First)).Response!;

    Assert.True(result.Truncated);
    Assert.Equal(201m, Assert.Single(result.Totals).Amount);
  }

  // The consistency contract: when the page is cut, the totals are a
  // second read, as of that read. A share written between the page and the
  // totals is counted in the totals - which never claim to be the sum of a
  // cut page - and a later reading shows it in the rows. An uncut page is
  // totalled from its own rows, one read.
  [Fact]
  public async Task AShareWrittenBetweenThePageAndTheTotalsIsCounted()
  {
    var between = new WriteBetweenReads();
    await using var f = await CostFixture.CreateAsync(between);
    await AddSharesAsync(f, 201, "toll", "USD");
    between.Arm(async () =>
    {
      await using var writer = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(f.Shared).Options
      );
      var expense = CostFixture.NewExpense("toll", 1m, "USD", f.Actor);
      writer.Expenses.Add(expense);
      writer.ExpenseAttributions.Add(
        new()
        {
          Id = Guid.NewGuid(),
          ExpenseId = expense.Id,
          DispatchId = f.First,
          Amount = 1m,
        }
      );
      await writer.SaveChangesAsync();
    });

    var result = (await f.CostsAsync(f.First)).Response!;

    Assert.True(between.Wrote);
    Assert.Equal(200, result.Rows.Count);
    Assert.Equal(202m, Assert.Single(result.Totals).Amount);
  }

  private static async Task AddSharesAsync(
    CostFixture f,
    int count,
    string kind,
    string currency
  )
  {
    for (var i = 0; i < count; i++)
    {
      var expense = await f.AddExpenseAsync(kind, 1m, currency);
      await f.AttributeAsync([new(f.First, 1m)], 0, expense);
    }
  }

  // Runs one write after the first page read of attributions has closed.
  private sealed class WriteBetweenReads : DbCommandInterceptor
  {
    private Func<Task>? write;
    public bool Wrote { get; private set; }

    public void Arm(Func<Task> action) => write = action;

    public override async ValueTask<InterceptionResult> DataReaderClosingAsync(
      DbCommand command,
      DataReaderClosingEventData eventData,
      InterceptionResult result
    )
    {
      if (
        write is { } action
        && command.CommandText.Contains("ExpenseAttributions")
        && command.CommandText.Contains("LIMIT")
      )
      {
        write = null;
        await action();
        Wrote = true;
      }
      return result;
    }
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
