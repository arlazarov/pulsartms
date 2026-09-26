using Application;
using Application.Caching;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Queries;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Application.Models;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Infrastructure;
using MediatR;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Eta;

// A conversation reads the ruleset of the driver's hours through the
// truck's planning summary, the same guarded read the Fleet Map shows,
// without its geometry. A forecast past its validity, a driver the truck
// is not assigned to, and a summary made for work the truck no longer has
// say nothing. The last is covered only once the summary's read copy of
// the truck's inputs has gone: that copy is not dropped by a work change
// and can lag a reassignment by up to ReadCacheSeconds (120 s), as it does
// for the Fleet Map. This test drops it explicitly to stand for that time.
[Trait("Category", "Eta")]
[Trait("Kind", "Integration")]
public sealed class DriverDutyRulesetTests
{
  [Theory]
  [InlineData(2, "CA")]
  [InlineData(-1, null)]
  public async Task OnlyACurrentSummaryOfTheDriversTruckNamesTheRuleset(
    int validForMinutes,
    string? expected
  )
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
    services.RemoveAll<IHosHistoryProvider>();
    services.AddSingleton<IHosHistoryProvider>(new NoHistory());
    await using var root = services.BuildServiceProvider();
    await using var seed = root.CreateAsyncScope();
    var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var driver = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "d-11005",
      Name = "Driver",
    };
    var passenger = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "d-other",
      Name = "Other driver",
    };
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "v-11005",
      UnitNumber = "11005",
      IsActive = true,
      DriverId = driver.Id,
    };
    var other = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "v-11006",
      UnitNumber = "11006",
      IsActive = true,
    };
    var id = Guid.NewGuid();
    var load = new Load
    {
      Id = id,
      LoadNumber = 1407,
      Status = "in_transit",
      TruckId = truck.Id,
      Stops =
      [
        new DispatchStop
        {
          Id = Guid.NewGuid(),
          DispatchId = id,
          Sequence = 1,
          Job = "Pick Up",
          TruckId = truck.Id,
          Latitude = 43.6m,
          Longitude = -79.4m,
        },
        new DispatchStop
        {
          Id = Guid.NewGuid(),
          DispatchId = id,
          Sequence = 2,
          Job = "Drop Off",
          TruckId = truck.Id,
          Latitude = 45.5m,
          Longitude = -73.6m,
        },
      ],
    };
    db.AddRange(driver, passenger, truck, other, load);
    await db.SaveChangesAsync();
    var snapshot = root.GetRequiredService<DriverHosSnapshot>();
    Assert.True(snapshot.TryBeginRefresh(keepWarm: true));
    snapshot.Complete(
      new Dictionary<string, DriverHosClocks>
      {
        [driver.ExternalId] = new()
        {
          UpdatedAt = DateTime.UtcNow,
          CurrentDutyStatus = "driving",
        },
        [passenger.ExternalId] = new()
        {
          UpdatedAt = DateTime.UtcNow,
          CurrentDutyStatus = "offDuty",
        },
      }
    );

    // The summary the background prepared for the truck's current work,
    // with a forecast that read the hours under Canadian rules.
    var cache = root.GetRequiredService<PlanningSummaryCache>();
    var key = new PlanningSummaryCache.Key(Company.Amf, truck.Id);
    await using (var prepare = root.CreateAsyncScope())
    {
      var work = await prepare
        .ServiceProvider.GetRequiredService<TruckPlanningInputsReader>()
        .ReadAsync(truck.Id, default);
      var signature = prepare
        .ServiceProvider.GetRequiredService<PlanningSummaryReader>()
        .Signature(work!);
      Assert.Null(cache.Read(key, signature));
      cache.Complete(
        cache.Take()!,
        signature,
        new(
          truck.Id,
          id,
          1407,
          new(new(), null, null, null, null, true)
          {
            Eta = new(
              DateTime.UtcNow.AddMinutes(-5),
              DateTime.UtcNow.AddMinutes(validForMinutes),
              [],
              null,
              []
            )
            {
              DutyStatus = new("driving", null, null, DateTimeOffset.UtcNow)
              {
                Jurisdiction = "CA",
              },
            },
          },
          null
        )
        {
          CalculatedAt = DateTimeOffset.UtcNow,
        }
      );
    }

    async Task<string?> Rules(Guid? on, Guid? who = null)
    {
      await using var scope = root.CreateAsyncScope();
      var result = await scope
        .ServiceProvider.GetRequiredService<
          IRequestHandler<
            GetDriverDutyStatusQuery,
            RequestResponse<DriverDutyView>
          >
        >()
        .Handle(new(who ?? driver.Id, on), default);
      return result.Response!.Jurisdiction;
    }

    Assert.Equal(expected, await Rules(truck.Id));
    // A driver on no single truck: nothing to read, nothing said.
    Assert.Null(await Rules(null));
    // A driver the truck is not assigned to: its forecast read someone
    // else's hours.
    Assert.Null(await Rules(truck.Id, passenger.Id));

    // The load goes to another truck. The summary still holds the old
    // forecast, but for work this truck no longer has.
    load.TruckId = other.Id;
    foreach (var stop in load.Stops)
      stop.TruckId = other.Id;
    await db.SaveChangesAsync();
    // As every command that changes work does after its commit. The read
    // copy of the truck's planning inputs is not dropped by that: it lives
    // up to ReadCacheSeconds (120 s), and until then this read, like the
    // Fleet Map, still gets the old summary. Dropping the copy here stands
    // for that time passing; it is not immediate invalidation.
    var reads = root.GetRequiredService<ReadCache>();
    foreach (var group in ReadGroups.Work)
      reads.Invalidate(group);
    reads.InvalidateItem("planning-inputs", truck.Id);
    Assert.Null(await Rules(truck.Id));
  }

  private sealed class NoHistory : IHosHistoryProvider
  {
    public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct) =>
      Task.FromResult<HosHistory?>(null);
  }
}
