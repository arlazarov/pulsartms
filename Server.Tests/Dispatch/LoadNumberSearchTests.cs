using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Dispatch.Services;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

// A load is searched for as the screens show it, AMF1408, as well as by
// its bare number (the owner, September 27). The board and the history
// read the number from one owner, so they cannot disagree.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class LoadNumberSearchTests
{
  [Theory]
  [InlineData("AMF1408", "AMF", 1408)]
  [InlineData(" amf 1408 ", "AMF", 1408)]
  [InlineData("AMF-1408", "AMF", 1408)]
  [InlineData("1408", "AMF", 1408)]
  [InlineData("#1408", null, 1408)]
  [InlineData("XYZ1408", "XYZ", 1408)]
  [InlineData("PO1408", "AMF", null)]
  [InlineData("AMF", "AMF", null)]
  [InlineData("Toronto", "AMF", null)]
  public void ADisplayedOrBareNumberNamesTheLoad(
    string search,
    string? prefix,
    int? number
  ) => Assert.Equal(number, LoadNumberSearch.Number(search, prefix));

  // The board finds trucks: the one carrying AMF1408, not another.
  [Fact]
  public void TheBoardFindsTheTruckCarryingALoadByItsDisplayedNumber()
  {
    static TruckDispatchBoardResponse Truck(string number, int load) =>
      new()
      {
        Key = number,
        TruckId = Guid.NewGuid(),
        TruckNumber = number,
        Dispatches = [new() { Id = Guid.NewGuid(), LoadNumber = load }],
      };
    var index = new DispatchBoardIndex(
      [Truck("11005", 1408), Truck("11006", 1500)]
    );

    var page = index.SelectPage(
      1,
      12,
      "AMF1408",
      null,
      DriverScope.All,
      LoadNumberSearch.Number("AMF1408", "AMF")
    );

    Assert.Equal("11005", Assert.Single(page.Items).TruckNumber);
  }

  [Theory]
  [InlineData(null, "AMF1408")]
  [InlineData("XYZ", "xyz1408")]
  public async Task HistoryFindsACompletedLoadByItsDisplayedNumber(
    string? savedPrefix,
    string search
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    if (savedPrefix is not null)
      db.DispatchSettings.Add(
        new()
        {
          Id = DispatchSettings.SingletonId,
          LoadNumberPrefix = savedPrefix,
        }
      );
    db.Dispatches.AddRange(
      new Load
      {
        Id = Guid.NewGuid(),
        LoadNumber = 1408,
        Status = "completed",
      },
      new Load
      {
        Id = Guid.NewGuid(),
        LoadNumber = 1409,
        Status = "completed",
      }
    );
    await db.SaveChangesAsync();
    using var planning = new PlanningTestServices(db);

    var found = await new GetDispatchQueryHandler(
      db,
      planning.Deadheads,
      new TestDriverScope()
    ).Handle(new(1, 10, search, "completed"), default);

    Assert.Equal(1408, Assert.Single(found.Response!.Items).LoadNumber);
  }
}
