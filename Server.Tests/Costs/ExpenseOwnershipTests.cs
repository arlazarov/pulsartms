using Application.Features.Costs.Models;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Costs;

[Trait("Category", "Costs")]
[Trait("Kind", "Integration")]
public sealed class ExpenseOwnershipTests
{
  [Fact]
  public async Task ForeignResourcesAndMissingLegsAreRejected()
  {
    await using var f = await CostFixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      CompanyId = Guid.NewGuid(),
      UnitNumber = "foreign",
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    var before = await f.Db.Expenses.CountAsync();
    var foreign = await f.RecordAsync(
      new("fuel", DateTime.UtcNow, 10m, "USD") { TruckId = truck.Id }
    );
    var missing = await f.RecordAsync(
      new("fuel", DateTime.UtcNow, 10m, "USD")
      {
        ExecutionLegId = Guid.NewGuid(),
      }
    );
    Assert.Equal(400, foreign.StatusCode);
    Assert.Equal(400, missing.StatusCode);
    Assert.Equal(before, await f.Db.Expenses.CountAsync());
  }

  [Fact]
  public async Task ForeignLoadAttributionDoesNotChangeExpenseOrHistory()
  {
    await using var f = await CostFixture.CreateAsync();
    var load = new Load
    {
      Id = Guid.NewGuid(),
      CompanyId = Guid.NewGuid(),
      LoadNumber = 1,
    };
    f.Db.Dispatches.Add(load);
    await f.Db.SaveChangesAsync();
    var result = await f.AttributeAsync([new(load.Id, 10m)], 0);
    Assert.Equal(400, result.StatusCode);
    Assert.Empty(await f.Db.ExpenseAttributions.ToListAsync());
    Assert.Empty(await f.Db.ExpenseAttributionEvents.ToListAsync());
    Assert.Equal(0, (await f.Db.Expenses.SingleAsync()).Revision);
  }

  [Fact]
  public async Task SameAmountCorrectionUpdatesMeaningAndHistory()
  {
    await using var f = await CostFixture.CreateAsync();
    await f.AttributeAsync([new(f.First, 100m)], 0);
    var result = await f.Attribution.Handle(
      new(
        f.ExpenseId,
        new(1, "contract", "Contract corrected", [new(f.First, 100m)])
      ),
      default
    );
    Assert.True(result.Success);
    var share = Assert.Single(result.Response!.Shares);
    Assert.Equal("contract", share.Basis);
    Assert.Equal("Contract corrected", share.Reason);
    Assert.False(share.ManualOverride);
    Assert.Equal(2, await f.Db.ExpenseAttributionEvents.CountAsync());
  }
}
