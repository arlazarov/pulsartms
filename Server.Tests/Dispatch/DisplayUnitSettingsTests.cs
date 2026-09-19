using System.Data;
using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Queries;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DisplayUnitSettingsTests
{
  [Theory]
  [InlineData("celsius", "kilometers")]
  [InlineData("fahrenheit", "miles")]
  [InlineData("both", "both")]
  public async Task UnitsPersistAcrossSessionsWithoutChangingPlanning(
    string temperature,
    string distance
  )
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var sender = fixture.Services.GetRequiredService<IMediator>();
    var initial = (await sender.Send(new GetDispatchSettingsQuery())).Response!;
    Assert.Equal("both", initial.TemperatureUnit);
    Assert.Equal("both", initial.DistanceUnit);
    var saved = await sender.Send(
      new UpdateDispatchSettingsCommand("AMF", 0, temperature, distance)
    );
    Assert.True(saved.Success);
    Assert.Equal(temperature, saved.Response!.TemperatureUnit);
    Assert.Equal(distance, saved.Response.DistanceUnit);
    await using var anotherSession = new AppDbContext(
      fixture.Services.GetRequiredService<DbContextOptions<AppDbContext>>()
    );
    Assert.Equal(
      saved.Response,
      (
        await new GetDispatchSettingsHandler(anotherSession).Handle(
          new(),
          default
        )
      ).Response
    );
    var unchanged = await sender.Send(
      new UpdateDispatchSettingsCommand(
        "AMF",
        saved.Response.Revision,
        temperature,
        distance
      )
    );
    Assert.Equal(saved.Response, unchanged.Response);
    var legacy = await sender.Send(
      new UpdateDispatchSettingsCommand("NEW", saved.Response.Revision)
    );
    Assert.Equal(temperature, legacy.Response!.TemperatureUnit);
    Assert.Equal(distance, legacy.Response.DistanceUnit);
    var stale = await sender.Send(
      new UpdateDispatchSettingsCommand(
        "AMF",
        saved.Response.Revision,
        "both",
        "both"
      )
    );
    Assert.Equal(409, stale.StatusCode);
    Assert.Empty(await anotherSession.FleetPlanningSettings.ToListAsync());
    Assert.Empty(await anotherSession.DispatchRoutePlans.ToListAsync());
    Assert.Empty(await anotherSession.SynchronizationCheckpoints.ToListAsync());
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Theory]
  [InlineData("C", "miles")]
  [InlineData("celsius", "km")]
  [InlineData("", "both")]
  [InlineData("both", "Miles")]
  public async Task UnsupportedUnitsAreRejectedBeforeStorage(
    string temperature,
    string distance
  )
  {
    await using var fixture = await PlanningPipelineFixture.CreateAsync();
    var result = await fixture
      .Services.GetRequiredService<IMediator>()
      .Send(new UpdateDispatchSettingsCommand("AMF", 0, temperature, distance));
    Assert.Equal(400, result.StatusCode);
    Assert.Empty(
      await fixture
        .Services.GetRequiredService<AppDbContext>()
        .DispatchSettings.ToListAsync()
    );
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public void MigrationAddsOnlyTwoDisplayColumnsWithBothAsTheExistingRowsDefault()
  {
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=unused;Database=unused;Username=unused")
        .Options
    );
    var migrations = db.Database.GetMigrations().ToArray();
    var index = Array.IndexOf(migrations, "20260912232808_AddDisplayUnits");
    Assert.True(index > 0);
    var sql = db.GetService<IMigrator>()
      .GenerateScript(migrations[index - 1], migrations[index]);
    Assert.Contains(
      "ADD \"DistanceUnit\" character varying(16) NOT NULL DEFAULT 'both'",
      sql
    );
    Assert.Contains(
      "ADD \"TemperatureUnit\" character varying(16) NOT NULL DEFAULT 'both'",
      sql
    );
    Assert.Equal(2, sql.Split("ALTER TABLE \"DispatchSettings\"").Length - 1);
    Assert.DoesNotContain("DROP", sql);
    Assert.DoesNotContain("DispatchRoutePlans", sql);
    Assert.False(db.Database.HasPendingModelChanges());
    Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
  }
}
