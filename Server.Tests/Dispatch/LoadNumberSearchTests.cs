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

  // The digits are the start of a load number, typed bare or displayed
  // (the owner, September 27): AMF10 and 10 both find 1014 and 1030, never
  // 2010 or 110. The board finds the trucks that carry them.
  [Fact]
  public void TheBoardFindsLoadsWhoseNumberBeginsWithTheDigits()
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
      [
        Truck("54777", 1014),
        Truck("11006", 1030),
        Truck("11007", 2010),
        Truck("22001", 110),
      ]
    );
    string[] Find(string search) =>
      index
        .SelectPage(
          1,
          12,
          search,
          null,
          DriverScope.All,
          LoadNumberSearch.Number(search, "AMF")
        )
        .Items.Select(x => x.TruckNumber)
        .Order()
        .ToArray();

    Assert.Equal(["11006", "54777"], Find("AMF10"));
    Assert.Equal(["11006", "54777"], Find("10"));
    Assert.Equal(["54777"], Find("AMF1014"));
  }

  // History reads the digits the same way, displayed or bare, with the
  // carrier's saved prefix or the default one.
  [Theory]
  [InlineData(null, "AMF10")]
  [InlineData(null, "10")]
  [InlineData("XYZ", "xyz10")]
  public async Task HistoryFindsCompletedLoadsWhoseNumberBeginsWithTheDigits(
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
    foreach (var number in new[] { 1014, 1030, 2010, 110 })
      db.Dispatches.Add(
        new Load
        {
          Id = Guid.NewGuid(),
          LoadNumber = number,
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

    Assert.Equal(
      [1014, 1030],
      found.Response!.Items.Select(x => x.LoadNumber).Order().ToArray()
    );
  }
}
