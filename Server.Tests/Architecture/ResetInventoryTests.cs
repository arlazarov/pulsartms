using System.Text.RegularExpressions;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Architecture;

// The reset script clears operational data and proves it left the identity
// boundary untouched. It names every table explicitly so an unknown one
// rejects instead of being truncated silently, which only works while the
// list matches the schema. It had fallen eleven tables behind, among them
// ExecutionLegStops and ExecutionLegRevisions, so past its own guard it would
// have cleared loads while leaving accepted execution behind them.
[Trait("Category", "Architecture")]
[Trait("Kind", "Architecture")]
public sealed class ResetInventoryTests
{
  [Fact]
  public void TheResetInventoryNamesEveryTableTheModelMaps()
  {
    var mapped = MappedTables();
    var (protectedTables, operational) = Inventory();
    var named = protectedTables.Concat(operational).ToHashSet();

    var missing = mapped.Except(named).Order().ToArray();

    Assert.True(
      missing.Length == 0,
      $"The reset script does not name {missing.Length} mapped tables: "
        + string.Join(", ", missing)
    );
  }

  [Fact]
  public void TheResetInventoryNamesNothingTheModelDoesNotMap()
  {
    var mapped = MappedTables();
    var (protectedTables, operational) = Inventory();
    // Identity and migration-history tables are created outside the model.
    var external = protectedTables.Except(mapped);

    var unknown = operational.Except(mapped).Order().ToArray();
    Assert.True(
      unknown.Length == 0,
      $"The reset script names {unknown.Length} tables the model no longer "
        + "maps: "
        + string.Join(", ", unknown)
    );
    Assert.All(
      external,
      name =>
        Assert.True(
          name.StartsWith("AspNet", StringComparison.Ordinal)
            || name is "DataProtectionKeys" or "__EFMigrationsHistory",
          $"{name} is protected but neither mapped nor an identity table."
        )
    );
  }

  // The guard refuses to run against a schema the inventory was not reviewed
  // for, which only works while it names the schema this code produces. It had
  // counted one migration too many: RebuildExecutionStorage is written across
  // two files, and counting files counted it twice, so the guard would have
  // refused every database that was actually up to date.
  [Fact]
  public void TheResetGuardNamesTheSchemaThisCodeProduces()
  {
    var migrations = Migrations();
    var sql = File.ReadAllText(
      Path.Combine(Root(), "scripts/sql/reset-core-storage.sql")
    );

    var count = Regex.Match(
      sql,
      @"__EFMigrationsHistory""\s*\)\s*<>\s*(?<count>\d+)"
    );
    Assert.True(count.Success, "The reset script checks no migration count.");
    Assert.Equal(migrations.Count, int.Parse(count.Groups["count"].Value));

    var latest = Regex.Match(
      sql,
      @"max\(""MigrationId""\).*?IS DISTINCT FROM\s*'(?<id>[^']+)'",
      RegexOptions.Singleline
    );
    Assert.True(latest.Success, "The reset script names no latest migration.");
    Assert.Equal(migrations[^1], latest.Groups["id"].Value);

    // The acknowledgement names the schema the operator states they reviewed,
    // so it has to move with the schema. Left behind, it would acknowledge an
    // inventory that no longer describes the database.
    var acknowledged = Regex.Match(
      sql,
      @"reset_ack.*?IS DISTINCT FROM\s*'(?<id>[^']+)'",
      RegexOptions.Singleline
    );
    Assert.True(acknowledged.Success, "The reset script asks for no token.");
    Assert.Equal(migrations[^1], acknowledged.Groups["id"].Value);
  }

  [Fact]
  public void TheIdentityBoundaryStaysProtected()
  {
    var (protectedTables, operational) = Inventory();

    foreach (
      var required in new[]
      {
        "Users",
        "AspNetUsers",
        "AspNetUserClaims",
        "AspNetUserLogins",
        "AspNetUserTokens",
        "AspNetUserRoles",
        "AspNetRoles",
        "AspNetRoleClaims",
        "DataProtectionKeys",
        "IntegrationCredentialSettings",
      }
    )
    {
      Assert.Contains(required, protectedTables);
      Assert.DoesNotContain(required, operational);
    }
  }

  private static IReadOnlyList<string> Migrations()
  {
    using var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open();
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    var migrations = db.Database.GetMigrations().Order().ToArray();
    Assert.NotEmpty(migrations);
    return migrations;
  }

  private static HashSet<string> MappedTables()
  {
    using var connection = new SqliteConnection("Data Source=:memory:");
    connection.Open();
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    return db
      .Model.GetEntityTypes()
      .Select(x => x.GetTableName())
      .Where(x => x is not null)
      .Select(x => x!)
      .ToHashSet(StringComparer.Ordinal);
  }

  private static (
    HashSet<string> Protected,
    HashSet<string> Operational
  ) Inventory()
  {
    var sql = File.ReadAllText(
      Path.Combine(Root(), "scripts/sql/reset-core-storage.sql")
    );
    return (Array(sql, "protected"), Array(sql, "operational"));
  }

  private static HashSet<string> Array(string sql, string name)
  {
    var match = Regex.Match(
      sql,
      name + @"\s+text\[\]\s*:=\s*ARRAY\[(?<body>[^\]]*)\]",
      RegexOptions.Singleline
    );
    Assert.True(match.Success, $"The reset script declares no {name} list.");
    return
    [
      .. match
        .Groups["body"]
        .Value.Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => x.Trim().Trim('\'')),
    ];
  }

  private static string Root()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (
      directory is not null
      && !File.Exists(Path.Combine(directory.FullName, "pulsartms.slnx"))
    )
      directory = directory.Parent;
    Assert.NotNull(directory);
    return directory!.FullName;
  }
}
