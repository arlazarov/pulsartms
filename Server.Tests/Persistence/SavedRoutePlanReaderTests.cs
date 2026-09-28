using System.Text.Json;
using Application.Features.Routing.Interfaces;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Persistence;

// A work list holds loads' own saved plans and execution legs' plans. Both
// kinds come back from one statement, each under its own key: a leg's plan
// is never a load's plan, nor a load's plan a leg's, whichever ids were
// asked for.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class SavedRoutePlanReaderTests
{
  [Fact]
  public async Task LoadsAndLegsComeFromOneReadUnderTheirOwnKeys()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var probe = new QueryColumnProbe();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .AddInterceptors(probe)
        .Options
    );
    await db.Database.EnsureCreatedAsync();

    await CheckAsync(db, probe);
  }

  // The metadata is read in raw SQL, which the company filter does not
  // reach. Another carrier asking by these very ids reads nothing; the
  // owner still reads them.
  [Fact]
  public async Task AnotherCarrierReadsNoneOfThesePlansByTheirIds()
  {
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var company = new TestCompany();
    await using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(connection)
        .UseApplicationServiceProvider(
          new ServiceCollection()
            .AddSingleton<ICurrentCompany>(company)
            .BuildServiceProvider()
        )
        .Options
    );
    await db.Database.EnsureCreatedAsync();

    await ForeignCheckAsync(db, company);
  }

  internal static async Task ForeignCheckAsync(
    AppDbContext db,
    TestCompany company
  )
  {
    var work = await SeedAsync(db);
    var reader = new SavedRoutePlanReader(
      db,
      NullLogger<SavedRoutePlanReader>.Instance
    );

    SavedRoutePlanMetadataSet foreign;
    SavedRoutePlanMetadata? single;
    using (company.As(Guid.NewGuid()))
    {
      foreign = await reader.ReadWorkAsync(
        [work.Plain, work.Carried],
        [work.Leg],
        default
      );
      single = await reader.ReadAsync(work.Carried, default);
    }
    var own = await reader.ReadWorkAsync(
      [work.Plain, work.Carried],
      [work.Leg],
      default
    );

    Assert.Empty(foreign.Loads);
    Assert.Empty(foreign.Legs);
    Assert.Null(single);
    Assert.Equal(2, own.Loads.Count);
    Assert.Single(own.Legs);
  }

  internal static async Task CheckAsync(AppDbContext db, QueryColumnProbe probe)
  {
    var work = await SeedAsync(db);
    var reader = new SavedRoutePlanReader(
      db,
      NullLogger<SavedRoutePlanReader>.Instance
    );
    probe.Clear();

    var both = await reader.ReadWorkAsync(
      [work.Plain, work.Carried],
      [work.Leg],
      default
    );

    Assert.Single(probe.Statements);
    Assert.Equal(
      new[] { work.Carried, work.Plain }.Order(),
      both.Loads.Keys.Order()
    );
    Assert.Equal(work.LegPlan, Assert.Single(both.Legs).Value.PlanId);
    Assert.Equal(work.Leg, both.Legs.Keys.Single());
    Assert.Equal(work.CarriedPlan, both.Loads[work.Carried].PlanId);
    var loadsOnly = await reader.ReadWorkAsync([work.Carried], [], default);
    Assert.Empty(loadsOnly.Legs);
    Assert.Equal(work.CarriedPlan, Assert.Single(loadsOnly.Loads).Value.PlanId);
    var legsOnly = await reader.ReadWorkAsync([], [work.Leg], default);
    Assert.Empty(legsOnly.Loads);
    Assert.Equal(work.LegPlan, Assert.Single(legsOnly.Legs).Value.PlanId);
  }

  private sealed record Work(
    Guid Plain,
    Guid Carried,
    Guid Leg,
    Guid CarriedPlan,
    Guid LegPlan
  );

  private static async Task<Work> SeedAsync(AppDbContext db)
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      CompanyId = Company.Amf,
      ExternalId = "plans",
      UnitNumber = "PLANS-1",
      IsActive = true,
    };
    var plain = Load(truck, 7101);
    var carried = Load(truck, 7102);
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      CompanyId = Company.Amf,
      TruckId = truck.Id,
      Trip = new() { Id = Guid.NewGuid(), CompanyId = Company.Amf },
      Status = "active",
      Revision = 1,
    };
    var carriedPlan = Guid.NewGuid();
    var legPlan = Guid.NewGuid();
    db.AddRange(truck, plain, carried, leg);
    db.DispatchRoutePlans.AddRange(
      Plan(truck, plain.Id, null, Guid.NewGuid()),
      Plan(truck, carried.Id, null, carriedPlan),
      Plan(truck, carried.Id, leg.Id, legPlan)
    );
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();
    return new(plain.Id, carried.Id, leg.Id, carriedPlan, legPlan);
  }

  private static Load Load(Truck truck, int number) =>
    new()
    {
      Id = Guid.NewGuid(),
      CompanyId = Company.Amf,
      LoadNumber = number,
      Status = "in_transit",
      TruckId = truck.Id,
    };

  private static DispatchRoutePlan Plan(
    Truck truck,
    Guid load,
    Guid? leg,
    Guid plan
  ) =>
    new()
    {
      Id = Guid.NewGuid(),
      CompanyId = Company.Amf,
      DispatchId = load,
      ExecutionLegId = leg,
      TruckId = truck.Id,
      InputHash = "inputs",
      PlanJson = JsonSerializer.Serialize(
        new
        {
          id = plan,
          dispatchId = load,
          executionLegId = leg,
          fromCurrentPosition = false,
          truckId = truck.Id,
          version = 1,
          tracking = new
          {
            passedStopIds = Array.Empty<Guid>(),
            visitedStops = new Dictionary<Guid, DateTime>(),
          },
        }
      ),
    };
}

[Trait("Category", "Database")]
[Trait("Kind", "Integration")]
public sealed class SavedRoutePlanReaderPostgresTests
{
  [RequiresPostgresFact]
  public async Task LoadsAndLegsComeFromOneReadUnderTheirOwnKeys()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var probe = new QueryColumnProbe();
    await using var db = fixture.Connect(probe);

    await SavedRoutePlanReaderTests.CheckAsync(db, probe);
  }

  [RequiresPostgresFact]
  public async Task AnotherCarrierReadsNoneOfThesePlansByTheirIds()
  {
    await using var fixture = await PostgresFixture.CreateAsync();
    var company = new TestCompany();
    await using var db = fixture.Connect(
      new ServiceCollection()
        .AddSingleton<ICurrentCompany>(company)
        .BuildServiceProvider()
    );

    await SavedRoutePlanReaderTests.ForeignCheckAsync(db, company);
  }
}
