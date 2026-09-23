using Application;
using Application.Features.Dispatch.Models;
using Application.Features.Routing.Background;
using Application.Features.Routing.Services.Routes;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

// Truck 11005: a load cancelled at the source, another taken. The cancelled
// load's work must stop being planned, the new load must become the truck's
// work, a calculation for the cancelled load that finishes late must not
// stand for the new one, and work planning refuses must say why rather than
// "updating" forever.
[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class SourceCancellationTests
{
  [Fact]
  public async Task WorkThatNeverStartedIsClosedWithItsCancelledLoad()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f, "1401");
    await SyncAsync(f);
    var leg = await f.Db.ExecutionLegs.AsNoTracking().SingleAsync();
    Assert.Equal("planned", leg.Status);

    source.Status = "cancelled";
    await SyncAsync(f);

    leg = await f.Db.ExecutionLegs.AsNoTracking().SingleAsync();
    Assert.Equal("cancelled", leg.Status);
    Assert.Null(leg.SourceReviewReason);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
    // Replaying the same cancelled source changes nothing further.
    await SyncAsync(f);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
  }

  [Fact]
  public async Task WorkAlreadyStartedIsNotClosedBehindAnyonesBack()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f, "1401");
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-1);
    await SyncAsync(f);
    Assert.Equal(
      "active",
      (await f.Db.ExecutionLegs.AsNoTracking().SingleAsync()).Status
    );

    source.Status = "cancelled";
    await SyncAsync(f);

    var leg = await f.Db.ExecutionLegs.AsNoTracking().SingleAsync();
    Assert.Equal("active", leg.Status);
    Assert.Contains("cancelled", leg.SourceReviewReason);
  }

  // AMF1407: in transit, picked up, both visits naming trailer 55904 that
  // no telemetry provider reports. The import catalogues it, so the load's
  // one resource set resolves and its execution is accepted.
  [Fact]
  public async Task AnInTransitLoadWithAnUnreportedTrailerBecomesAcceptedWork()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f, "1407");
    source.Status = "in_transit";
    source.TrailerNumber = "55904";
    foreach (var stop in source.Stops)
      stop.TrailerNumber = "55904";
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-4);

    await SyncAsync(f);

    Assert.Null(
      (await f.Db.DispatchSourceLinks.SingleAsync()).ExecutionReviewReason
    );
    var trailer = await f.Db.Trailers.SingleAsync();
    var leg = await f.Db.ExecutionLegs.AsNoTracking().SingleAsync();
    Assert.Equal(("active", trailer.Id), (leg.Status, leg.TrailerId));
  }

  // The consumer was calculating for the cancelled load when the truck's
  // work became the new load. Its late result is not the new load's.
  [Fact]
  public async Task ALateCalculationForTheCancelledLoadIsNotTheNewLoads()
  {
    var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
    var cache = new PlanningSummaryCache(time);
    var key = new PlanningSummaryCache.Key(Company.Amf, Guid.NewGuid());
    Assert.Null(cache.Read(key, "work:1401"));
    var cancelled = cache.Take()!;

    // The source cancelled 1401; the truck's inputs now name 1407.
    Assert.Null(cache.Read(key, "work:1407"));
    cache.Complete(
      cancelled,
      "work:1401",
      new(key.Truck, Guid.NewGuid(), 1401, null, null)
      {
        CalculatedAt = time.GetUtcNow(),
      }
    );

    Assert.Null(cache.Read(key, "work:1407"));
    Assert.Equal("work:1407", cache.Take()!.Signature);
  }

  [Fact]
  public async Task WorkPlanningRefusesSaysWhyInsteadOfUpdatingForever()
  {
    // The application as the server composes it, over SQLite; nothing here
    // reaches a provider, because planning refuses before routing.
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
    await using var root = services.BuildServiceProvider();
    await using var seed = root.CreateAsyncScope();
    var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "v-11005",
      UnitNumber = "11005",
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
          Latitude = 42.9m,
          Longitude = -77.9m,
          PickedUpAt = DateTime.UtcNow.AddHours(-4),
        },
        new DispatchStop
        {
          Id = Guid.NewGuid(),
          DispatchId = id,
          Sequence = 2,
          Job = "Drop Off",
          TruckId = truck.Id,
          Latitude = 39.9m,
          Longitude = -76.7m,
        },
      ],
    };
    db.AddRange(truck, load);
    db.DispatchSourceLinks.Add(
      new DispatchSourceLink
      {
        Provider = "source",
        ExternalId = "1407",
        DispatchId = id,
        Dispatch = load,
        ExecutionReviewReason = "Review the initial assignment.",
      }
    );
    await db.SaveChangesAsync();

    AutomaticPlanningResult Read()
    {
      using var scope = root.CreateScope();
      return scope
        .ServiceProvider.GetRequiredService<PlanningSummaryReader>()
        .ForTruckAsync(truck.Id, default)
        .GetAwaiter()
        .GetResult();
    }
    Assert.True(Read().IsRefreshing);
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var running = root.GetRequiredService<IPlanningSummaryOperation>()
      .RunAsync(stop.Token);
    try
    {
      AutomaticPlanningResult result;
      while ((result = Read()).Message == "Planning summary is updating.")
        await Task.Delay(50, stop.Token);
      Assert.Contains("Review", result.Message);
      Assert.Equal(id, result.DispatchId);
      Assert.Null(result.State);
    }
    finally
    {
      stop.Cancel();
      try
      {
        await running;
      }
      catch (OperationCanceledException) { }
    }
  }

  private static async Task<ExternalDispatch> SeedAsync(
    DispatchSyncFixture f,
    string number
  )
  {
    f.Db.Trucks.Add(
      new()
      {
        Id = Guid.NewGuid(),
        UnitNumber = "11005",
        ExternalId = "11005",
        IsActive = true,
      }
    );
    await f.Db.SaveChangesAsync();
    var source = new ExternalDispatch
    {
      ExternalId = number,
      LoadNumber = int.Parse(number),
      Status = "assigned",
      TruckNumber = "11005",
      Stops = new[] { "Pick Up", "Delivery" }
        .Select(
          (job, i) =>
            new ExternalDispatchStop
            {
              Sequence = i + 1,
              Job = job,
              Name = $"Visit {i}",
              Address = $"{i + 1} Main Road",
              City = "Toronto",
              Province = "ON",
              Country = "CA",
              TruckNumber = "11005",
            }
        )
        .ToList(),
    };
    f.Sources.Add(source);
    return source;
  }

  private static async Task SyncAsync(DispatchSyncFixture f)
  {
    f.Memory.Compact(1);
    f.Db.ChangeTracker.Clear();
    var result = await f.Handler.Handle(new(), default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
  }
}
