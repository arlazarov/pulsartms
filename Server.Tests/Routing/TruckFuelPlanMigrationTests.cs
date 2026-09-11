using System.Data;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TruckFuelPlanMigrationTests
{
  private const string Migration = "20260909020907_StoreTruckFuelPlans";

  [Fact]
  public void PostgreSqlModelMatchesTheSavedMigrationSnapshotWithoutConnecting()
  {
    using var db = Context();

    Assert.False(db.Database.HasPendingModelChanges());
    Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
  }

  [Fact]
  public void PostgreSqlUpgradeAddsOnlyTheTruckSnapshotTableAndIndexesWithoutConnecting()
  {
    using var db = Context();
    var migrations = db.Database.GetMigrations().ToArray();
    var index = Array.IndexOf(migrations, Migration);
    Assert.True(index > 0);

    var sql = db.GetService<IMigrator>().GenerateScript(migrations[index - 1], Migration);

    Assert.Contains("CREATE TABLE \"TruckFuelPlans\"", sql);
    Assert.Contains("CREATE UNIQUE INDEX \"IX_TruckFuelPlans_TruckId\"", sql);
    Assert.Contains("timestamp with time zone", sql);
    Assert.Contains("REFERENCES \"Trucks\"", sql);
    Assert.DoesNotContain("DROP TABLE", sql);
    Assert.DoesNotContain("ALTER TABLE \"DispatchRoutePlans\"", sql);
    Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
  }

  private static AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql("Host=unused;Database=unused;Username=unused").Options);
}
