using System.Data;
using Application.Features.Users.Commands;
using Application.Features.Users.Queries;
using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Server.Tests.Identity;

[Trait("Category", "Identity")]
[Trait("Kind", "Integration")]
public sealed class AppearanceSettingsTests
{
  [Fact]
  public async Task ThemePersistsAcrossContextsAndOnlyChangesTheCaller()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>()
      .UseSqlite(connection)
      .Options;
    await using (var db = new AppDbContext(options))
    {
      await db.Database.EnsureCreatedAsync();
      db.Users.AddRange(
        new User { IdentityUserId = "first" },
        new User { IdentityUserId = "second" }
      );
      await db.SaveChangesAsync();
      var result = await new UpdateAppearanceSettingsHandler(
        db,
        new Caller(true, "first")
      ).Handle(new("dark", "celsius", "kilometers"), default);
      Assert.True(result.Success);
    }
    await using var fresh = new AppDbContext(options);
    // "second" never chose: no theme, and the Client applies the product
    // default (dark). It used to read back as light, indistinguishable from
    // a choice; the owner asked for dark by default.
    foreach (
      var (identity, theme) in new[]
      {
        ("first", "dark"),
        ("second", (string?)null),
      }
    )
    {
      var result = await new GetAppearanceSettingsHandler(
        fresh,
        new Caller(true, identity)
      ).Handle(new(), default);
      Assert.True(result.Success);
      Assert.Equal(theme, result.Response!.Theme);
      Assert.Equal(
        identity == "first" ? "celsius" : "both",
        result.Response.TemperatureUnit
      );
      Assert.Equal(
        identity == "first" ? "kilometers" : "both",
        result.Response.DistanceUnit
      );
    }
    Assert.True(
      (
        await new UpdateAppearanceSettingsHandler(
          fresh,
          new Caller(true, "first")
        ).Handle(new("light"), default)
      ).Success
    );
    fresh.ChangeTracker.Clear();
    Assert.Equal(
      [("first", "light"), ("second", (string?)null)],
      await fresh
        .Users.OrderBy(x => x.IdentityUserId)
        .Select(x => ValueTuple.Create(x.IdentityUserId, x.Theme))
        .ToListAsync()
    );
    var preserved = await fresh.Users.SingleAsync(x =>
      x.IdentityUserId == "first"
    );
    Assert.Equal("celsius", preserved.TemperatureUnit);
    Assert.Equal("kilometers", preserved.DistanceUnit);
  }

  [Theory]
  [InlineData(false, "active")]
  [InlineData(true, null)]
  [InlineData(true, "missing")]
  [InlineData(true, "inactive")]
  public async Task ReadsAndWritesRequireAnActiveAuthenticatedProfile(
    bool authenticated,
    string? identity
  )
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    db.Users.Add(new User { IdentityUserId = "inactive", IsActive = false });
    await db.SaveChangesAsync();
    var caller = new Caller(authenticated, identity);
    Assert.Equal(
      401,
      (
        await new GetAppearanceSettingsHandler(db, caller).Handle(
          new(),
          default
        )
      ).StatusCode
    );
    Assert.Equal(
      401,
      (
        await new UpdateAppearanceSettingsHandler(db, caller).Handle(
          new("dark"),
          default
        )
      ).StatusCode
    );
    Assert.Null((await db.Users.SingleAsync()).Theme);
  }

  [Theory]
  [InlineData("system")]
  [InlineData("Dark")]
  [InlineData("")]
  [InlineData(null)]
  public async Task InvalidThemesAreRejectedBeforePersistence(string? theme)
  {
    var command = new UpdateAppearanceSettingsCommand(theme!);
    Assert.NotEmpty(command.Wrong());
    var result = await new UpdateAppearanceSettingsHandler(
      null!,
      new Caller(true, "first")
    ).Handle(command, default);
    Assert.Equal(400, result.StatusCode);
  }

  [Theory]
  [InlineData("kelvin", "miles")]
  [InlineData("celsius", "meters")]
  [InlineData("", "both")]
  [InlineData("both", "")]
  public async Task InvalidUnitsAreRejectedBeforePersistence(
    string temperature,
    string distance
  )
  {
    var command = new UpdateAppearanceSettingsCommand(
      "light",
      temperature,
      distance
    );
    Assert.NotEmpty(command.Wrong());
    var result = await new UpdateAppearanceSettingsHandler(
      null!,
      new Caller(true, "first")
    ).Handle(command, default);
    Assert.Equal(400, result.StatusCode);
  }

  [Fact]
  public void MigrationSeedsExistingAccountsWithoutRemovingCompanyData()
  {
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=unused;Database=unused;Username=unused")
        .Options
    );
    var sql = db.GetService<IMigrator>()
      .GenerateScript(
        "20260913135741_AddUserTheme",
        "20260913142839_AddUserDisplayUnits"
      );
    Assert.Equal(2, sql.Split("ALTER TABLE \"Users\"").Length - 1);
    Assert.Contains("UPDATE \"Users\"", sql);
    Assert.Contains("FROM \"DispatchSettings\" AS s", sql);
    Assert.Contains("731f4d30-c85a-4af0-a14a-29db18bd4a47", sql);
    Assert.Contains("DEFAULT 'both'", sql);
    Assert.DoesNotContain("DROP", sql);
    Assert.DoesNotContain("DispatchRoutePlans", sql);
    Assert.False(db.Database.HasPendingModelChanges());
    Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
  }

  // The owner asked for dark by default. A theme nobody chose is now null -
  // the Client applies the default - and the migration only lets it be:
  // it changes no saved choice, light or dark.
  [Fact]
  public void UnchosenThemesBecomeNullWithoutChangingSavedChoices()
  {
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=unused;Database=unused;Username=unused")
        .Options
    );
    var sql = db.GetService<IMigrator>()
      .GenerateScript(
        "20260927230607_KeepEarlyDeliveryStatuses",
        "20260927233013_LetThemeBeUnchosen"
      );
    Assert.Contains("DROP NOT NULL", sql);
    Assert.Contains("DROP DEFAULT", sql);
    Assert.DoesNotContain("UPDATE", sql);
    Assert.False(db.Database.HasPendingModelChanges());
  }

  private sealed record Caller(bool IsAuthenticated, string? IdentityUserId)
    : ICurrentUser;
}
