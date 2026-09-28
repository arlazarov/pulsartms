using System.Text.Json;
using Application;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Queries;
using Application.Features.Fleet.Services;
using Application.Interfaces;
using Application.Models;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Infrastructure;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Server.Tests.Eta;

// The truck panel reads its driver's duty without a route: the status and
// its start, the ongoing rest and when it completes a daily rest, all from
// the driver's history and clocks. The cycle reset needs the ruleset, which
// only a current forecast names, so without one it stays unknown rather
// than guessed. The daily rest length is the forecasts' own.
[Trait("Category", "Eta")]
[Trait("Kind", "Integration")]
public sealed class TruckDutyStatusReadTests
{
  [Fact]
  public async Task ARestingDriverOnATruckWithNoRouteGetsTheServersRestReading()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var services = new ServiceCollection();
    var configuration = new ConfigurationBuilder().Build();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddApplication();
    services.AddInfrastructure(configuration);
    services.RemoveAll<DbContextOptions<AppDbContext>>();
    services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
    services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
    services.RemoveAll<ICurrentCompany>();
    services.AddSingleton<ICurrentCompany>(new TestCompany());
    var now = DateTimeOffset.UtcNow;
    var history = new FixedHistory(
      new(
        now.AddDays(-2),
        now,
        "Etc/UTC",
        0,
        null,
        null,
        [
          new(now.AddHours(-6), now.AddHours(-4), "driving"),
          new(now.AddHours(-4), now.AddHours(-3), "offDuty"),
          new(now.AddHours(-3), now, "sleeperBerth"),
        ]
      )
    );
    services.RemoveAll<IHosHistoryProvider>();
    services.AddSingleton<IHosHistoryProvider>(history);
    await using var root = services.BuildServiceProvider();
    await using var seed = root.CreateAsyncScope();
    var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "d-54777",
      Name = "Driver",
    };
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "v-54777",
      UnitNumber = "54777",
      IsActive = true,
      DriverId = driver.Id,
    };
    var empty = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "v-11009",
      UnitNumber = "11009",
      IsActive = true,
    };
    db.AddRange(driver, truck, empty);
    await db.SaveChangesAsync();
    var snapshot = root.GetRequiredService<DriverHosSnapshot>();
    Assert.True(snapshot.TryBeginRefresh(keepWarm: true));
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks>
      {
        [driver.ExternalId] = new()
        {
          UpdatedAt = now.UtcDateTime,
          CurrentDutyStatus = "sleeperBerth",
        },
      }
    );

    async Task<RequestResponse<TruckDutyStatusView>> Read(Guid id)
    {
      await using var scope = root.CreateAsyncScope();
      return await scope
        .ServiceProvider.GetRequiredService<
          IRequestHandler<
            GetTruckDutyStatusQuery,
            RequestResponse<TruckDutyStatusView>
          >
        >()
        .Handle(new(id), default);
    }

    var read = await Read(truck.Id);
    Assert.True(read.Success);
    Assert.Equal(driver.Id, read.Response!.DriverId);
    var duty = Assert.IsType<DriverDutyStatus>(read.Response.Duty);
    Assert.Equal("sleeperBerth", duty.Status);
    Assert.Equal(now.AddHours(-3), duty.StatusStartedAt);
    // Off duty then sleeper: one rest, begun four hours ago.
    Assert.Equal(now.AddHours(-4), duty.RestStartedAt);
    Assert.Equal(
      now.AddHours(DriverDutyStatus.DailyRestHours - 4),
      duty.DailyRestCompleteAt
    );
    Assert.Equal(
      (DriverDutyStatus.DailyRestHours - 4) * 60,
      duty.DailyRestRemainingMinutes
    );
    // No current forecast: the ruleset, and with it the cycle reset, is
    // unknown rather than guessed.
    Assert.Null(duty.Jurisdiction);
    Assert.Null(duty.CycleResetCompleteAt);

    // The client reads these as sent; they are not recomputed there.
    var json = JsonSerializer.Serialize(
      read.Response,
      new JsonSerializerOptions(JsonSerializerDefaults.Web)
    );
    Assert.Contains("\"dailyRestCompleteAt\":", json);
    Assert.Contains("\"dailyRestRemainingMinutes\":360", json);
    Assert.Contains("\"cycleResetCompleteAt\":null", json);
    Assert.DoesNotContain("tenHour", json, StringComparison.OrdinalIgnoreCase);

    var none = await Read(empty.Id);
    Assert.True(none.Success);
    Assert.Null(none.Response!.DriverId);
    Assert.Null(none.Response.Duty);

    Assert.Equal(404, (await Read(Guid.NewGuid())).StatusCode);
  }

  private sealed class FixedHistory(HosHistory value) : IHosHistoryProvider
  {
    public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct) =>
      Task.FromResult<HosHistory?>(value);
  }
}
