using Application.Caching;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Domain.Entities;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Server.Tests.Persistence;

using Load = global::Domain.Entities.Dispatch.Dispatch;

// The question this whole thing exists to answer: can one carrier see
// another carrier's work? Asked of the database, not of the code that
// talks to it, because the point of the filters is that no code has to
// remember anything.
[Trait("Category", "Persistence")]
[Trait("Kind", "Integration")]
public sealed class CompanyIsolationTests
{
  private static readonly Guid Amf = Company.Amf;
  private static readonly Guid Other = new(
    "b1e1b1e1-0000-4000-8000-000000000002"
  );

  [Fact]
  public async Task ACarrierNeverSeesAnotherCarriersLoads()
  {
    await using var world = await TwoCarriersAsync();

    var mine = await world.As(Amf).Dispatches.ToListAsync();
    var theirs = await world.As(Other).Dispatches.ToListAsync();

    Assert.Equal(["AMF-1"], mine.Select(x => x.OrderNumber));
    Assert.Equal(["OTHER-1"], theirs.Select(x => x.OrderNumber));
  }

  [Fact]
  public async Task AskingForAnotherCarriersLoadByItsOwnIdFindsNothing()
  {
    await using var world = await TwoCarriersAsync();
    var theirId = (await world.As(Other).Dispatches.SingleAsync()).Id;

    Assert.Null(
      await world.As(Amf).Dispatches.SingleOrDefaultAsync(x => x.Id == theirId)
    );
  }

  [Fact]
  public async Task ANewRowIsStampedWithTheCarrierThatWroteItWithoutBeingTold()
  {
    await using var world = await TwoCarriersAsync();
    var db = world.As(Other);

    db.Trucks.Add(new() { Id = Guid.NewGuid(), UnitNumber = "T-9" });
    await db.SaveChangesAsync();

    var saved = await world
      .Everything.Trucks.IgnoreQueryFilters()
      .SingleAsync(x => x.UnitNumber == "T-9");
    Assert.Equal(Other, saved.CompanyId);
  }

  [Fact]
  public async Task WorkThatNeverSaidWhoseCarrierItIsForSeesNothingAtAll()
  {
    await using var world = await TwoCarriersAsync();

    // Not "everybody's loads" - none. A background pass that forgot to
    // choose a carrier must not read across all of them.
    Assert.Empty(await world.As(null).Dispatches.ToListAsync());
  }

  [Fact]
  public async Task SharedTablesAreVisibleToEveryCarrier()
  {
    await using var world = await TwoCarriersAsync();
    var seed = world.Everything;
    seed.IftaTaxRates.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Jurisdiction = "ON",
        FuelType = "diesel",
        Currency = "CAD",
        Unit = "litre",
      }
    );
    await seed.SaveChangesAsync();

    Assert.Single(await world.As(Amf).IftaTaxRates.ToListAsync());
    Assert.Single(await world.As(Other).IftaTaxRates.ToListAsync());
  }

  [Fact]
  public async Task UnselectedAndForeignOwnedWritesAreRejected()
  {
    await using var world = await TwoCarriersAsync();
    var unselected = world.As(null);
    unselected.Trucks.Add(new() { Id = Guid.NewGuid() });
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => unselected.SaveChangesAsync()
    );
    var db = world.As(Amf);
    db.Trucks.Add(new() { Id = Guid.NewGuid(), CompanyId = Other });
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => db.SaveChangesAsync()
    );
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ATrackedForeignRowCannotBeUpdatedOrDeleted(bool deleting)
  {
    await using var world = await TwoCarriersAsync();
    var db = world.As(Amf);
    var row = await db
      .Dispatches.IgnoreQueryFilters()
      .SingleAsync(x => x.CompanyId == Other);
    if (deleting)
      db.Dispatches.Remove(row);
    else
      row.OrderNumber = "Changed";
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => db.SaveChangesAsync()
    );
  }

  [Fact]
  public async Task ChangingTheOwnerDoesNotAuthorizeAForeignWrite()
  {
    await using var world = await TwoCarriersAsync();
    var db = world.As(Amf);
    var row = await db
      .Dispatches.IgnoreQueryFilters()
      .SingleAsync(x => x.CompanyId == Other);
    row.CompanyId = Amf;
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => db.SaveChangesAsync()
    );
  }

  [Fact]
  public async Task FleetMetadataCacheDoesNotReuseAnotherCompanysCatalog()
  {
    await using var world = await TwoCarriersAsync();
    world.Everything.Trucks.AddRange(
      new()
      {
        Id = Guid.NewGuid(),
        CompanyId = Amf,
        UnitNumber = "mine",
      },
      new()
      {
        Id = Guid.NewGuid(),
        CompanyId = Other,
        UnitNumber = "theirs",
      }
    );
    await world.Everything.SaveChangesAsync();
    var companies = new TestCompany();
    using var reads = new ReadCache(
      Options.Create(new SynchronizationOptions()),
      companies
    );
    var cache = new FleetCache(reads);
    Assert.Equal(
      "mine",
      Assert.Single(await cache.GetAsync(world.As(Amf))).UnitNumber
    );
    using (companies.As(Other))
      Assert.Equal(
        "theirs",
        Assert.Single(await cache.GetAsync(world.As(Other))).UnitNumber
      );
    Assert.Equal(
      "mine",
      Assert.Single(await cache.GetAsync(world.As(Amf))).UnitNumber
    );
  }

  private static async Task<World> TwoCarriersAsync()
  {
    var world = new World();
    var db = world.Everything;
    await db.Database.EnsureCreatedAsync();
    db.Companies.AddRange(
      new()
      {
        Id = Amf,
        Key = "amfcarrier",
        Name = "AMF Carrier",
      },
      new()
      {
        Id = Other,
        Key = "other",
        Name = "Other Carrier",
      }
    );
    db.Dispatches.AddRange(
      new Load
      {
        Id = Guid.NewGuid(),
        OrderNumber = "AMF-1",
        CompanyId = Amf,
      },
      new Load
      {
        Id = Guid.NewGuid(),
        OrderNumber = "OTHER-1",
        CompanyId = Other,
      }
    );
    await db.SaveChangesAsync();
    return world;
  }

  // One in-memory database, reached either with the filters on behalf of a
  // named carrier, or without them to set the scene and check the result.
  private sealed class World : IAsyncDisposable
  {
    private readonly Microsoft.Data.Sqlite.SqliteConnection connection = new(
      "DataSource=:memory:"
    );
    private readonly List<AppDbContext> opened = [];
    private readonly Company current = new();

    public World() => connection.Open();

    // Built by hand, the way a migration or a tool builds one: it serves
    // the single carrier and its filters can be waved off to check what
    // actually landed in the table.
    private AppDbContext? everything;
    public AppDbContext Everything =>
      everything ??= Open(null, services: false);

    public AppDbContext As(Guid? company) => Open(company, services: true);

    private AppDbContext Open(Guid? company, bool services)
    {
      current.Chosen = company;
      var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(
        connection
      );
      if (services)
        builder.UseApplicationServiceProvider(
          new ServiceCollection()
            .AddSingleton<ICurrentCompany>(current)
            .BuildServiceProvider()
        );
      var db = new AppDbContext(builder.Options);
      opened.Add(db);
      return db;
    }

    public async ValueTask DisposeAsync()
    {
      foreach (var db in opened)
        await db.DisposeAsync();
      await connection.DisposeAsync();
    }

    private sealed class Company : ICurrentCompany
    {
      public Guid? Chosen { get; set; }

      public Guid? Id => Chosen;

      public IDisposable As(Guid company)
      {
        Chosen = company;
        return new Nothing();
      }

      private sealed class Nothing : IDisposable
      {
        public void Dispose() { }
      }
    }
  }
}
