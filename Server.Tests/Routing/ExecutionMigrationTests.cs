using System.Data;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class ExecutionMigrationTests
{
  private const string Previous = "20260914022232_AddExecutionAndMileage";
  private const string Current =
    "20260914031300_FinishSwitchAndAutomaticMileage";

  [Fact]
  public void UpgradePreservesExistingManualOriginAndAddsDurableWork()
  {
    using var db = Context();
    var sql = db.GetService<IMigrator>().GenerateScript(Previous, Current);
    Assert.Contains("DEFAULT 'manual'", sql);
    Assert.Contains("CREATE TABLE \"ExecutionPlanningChanges\"", sql);
    Assert.Contains("CREATE TABLE \"OdometerIntervals\"", sql);
    Assert.Contains("CREATE TABLE \"ExecutionSourceReceipts\"", sql);
    Assert.Contains("WHERE NOT \"IsCancelled\"", sql);
    Assert.DoesNotContain("DROP TABLE", sql);
    Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
  }

  [Fact]
  public void DowngradeRefusesToDiscardNativeHistory()
  {
    using var db = Context();
    var sql = db.GetService<IMigrator>().GenerateScript(Current, Previous);
    var guard = sql.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal);
    var drop = sql.IndexOf("DROP TABLE", StringComparison.Ordinal);
    Assert.True(guard >= 0 && drop > guard);
    Assert.Contains("Execution history exists; use a forward repair.", sql);
    Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
  }

  private static AppDbContext Context() =>
    new(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=unused;Database=unused;Username=unused")
        .Options
    );
}
