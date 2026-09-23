using System.Data.Common;
using Application.Features.Fleet.Commands.SyncFleet;
using Application.Features.Fleet.Services;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Fleet;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Fleet;

// Which trailer a truck has now. Telemetry is preferred when it is certain;
// the truck's current work stands in when it is not. A missing record is not
// a detach, planned and finished loads say nothing, and no source silently
// displaces another.
[Trait("Category", "Fleet")]
[Trait("Kind", "Integration")]
public sealed class TruckTrailerAssignmentTests
{
  private const string Source = "acme-telematics";

  // Truck 11005: in transit on a load that names 55904 at both stops, with
  // no trailer from telemetry and 55904 unknown to the catalog.
  [Fact]
  public async Task ACurrentLoadsUnknownTrailerIsCataloguedAndPutOnItsTruck()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var truck = await TruckAsync(f, "11005");
    await LoadAsync(f, truck, "in_transit", "55904", provider: "loads");

    var changed = await ResolveAsync(f);

    var trailer = await f.Db.Trailers.SingleAsync();
    Assert.Equal(
      ("55904", "", "loads"),
      (trailer.UnitNumber, trailer.ExternalId, trailer.Source)
    );
    var row = await f.Db.Trucks.AsNoTracking().SingleAsync();
    Assert.Equal((trailer.Id, "load"), (row.TrailerId, row.TrailerSource));
    Assert.Equal([truck.Id], changed);
    // Nothing moved: a second pass writes nothing.
    Assert.Empty(await ResolveAsync(f));
  }

  [Fact]
  public async Task TelemetryAloneAssignsAndIsPreferredOverTheLoad()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var truck = await TruckAsync(f, "11005", driver: "d-1");
    await TrailerCatalog.ApplyAsync(
      f.Db,
      Source,
      [Reported("t-1", "70001")],
      default
    );
    await f.Db.SaveChangesAsync();
    await SyncAsync(f, ("d-1", "t-1", null));
    var row = await f.Db.Trucks.AsNoTracking().SingleAsync();
    Assert.Equal("telemetry", row.TrailerSource);
    Assert.Equal(
      "70001",
      (await f.Db.Trailers.FindAsync(row.TrailerId))!.UnitNumber
    );

    // The load says 55904: telemetry keeps the truck, the load is a conflict.
    await LoadAsync(f, truck, "in_transit", "55904");
    await ResolveAsync(f);
    row = await f
      .Db.Trucks.AsNoTracking()
      .Include(x => x.TrailerConflict)
      .SingleAsync();
    Assert.Equal("telemetry", row.TrailerSource);
    Assert.Equal("55904", row.TrailerConflict!.UnitNumber);
  }

  [Fact]
  public async Task AMissingRecordFallsBackButAnEndedOneDetaches()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var truck = await TruckAsync(f, "11005", driver: "d-1");
    await TrailerCatalog.ApplyAsync(
      f.Db,
      Source,
      [Reported("t-1", "70001")],
      default
    );
    await f.Db.SaveChangesAsync();
    await LoadAsync(f, truck, "in_transit", "55904");
    await SyncAsync(f, ("d-1", "t-1", null));

    // The provider reports no current record: not known, so the load
    // stands in rather than the truck losing its trailer.
    await SyncAsync(f);
    var row = await f
      .Db.Trucks.AsNoTracking()
      .Include(x => x.Trailer)
      .SingleAsync();
    Assert.Equal(
      ("55904", "load"),
      (row.Trailer!.UnitNumber, row.TrailerSource)
    );

    // The provider says the assignment ended: an explicit detach.
    await SyncAsync(f, ("d-1", "t-1", DateTime.UtcNow.AddMinutes(-5)));
    row = await f
      .Db.Trucks.AsNoTracking()
      .Include(x => x.TrailerConflict)
      .SingleAsync();
    Assert.Null(row.TrailerId);
    Assert.Equal("55904", row.TrailerConflict!.UnitNumber);
  }

  [Fact]
  public async Task OnlyTheLoadInTransitAtItsCurrentStopCounts()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var truck = await TruckAsync(f, "11005");
    await LoadAsync(f, truck, "assigned", "11111");
    await LoadAsync(f, truck, "delivered", "22222");
    await LoadAsync(f, truck, "in_transit", "33333", completedStops: 2);
    await LoadAsync(
      f,
      truck,
      "in_transit",
      "55904",
      stopTrailer: "44444",
      completedStops: 1
    );
    await ResolveAsync(f);
    var row = await f
      .Db.Trucks.AsNoTracking()
      .Include(x => x.Trailer)
      .SingleAsync();
    // The second stop of the live load names 44444; the finished, planned
    // and delivered loads say nothing.
    Assert.Equal("44444", row.Trailer!.UnitNumber);

    // Two live loads naming different trailers answer nothing.
    await LoadAsync(f, truck, "in_transit", "66666");
    await ResolveAsync(f);
    Assert.Null((await f.Db.Trucks.AsNoTracking().SingleAsync()).TrailerId);
  }

  [Fact]
  public async Task TheDispatchersDecisionsAndUnavailableTrailersAreRespected()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var imported = await TruckAsync(f, "11005");
    var chosen = await TruckAsync(f, "11006");
    var load = await LoadAsync(f, imported, "in_transit", "55904");
    load.PlanningTruckId = chosen.Id;
    await f.Db.SaveChangesAsync();
    await ResolveAsync(f);
    Assert.Null(
      (
        await f.Db.Trucks.AsNoTracking().SingleAsync(x => x.Id == imported.Id)
      ).TrailerId
    );
    Assert.NotNull(
      (
        await f.Db.Trucks.AsNoTracking().SingleAsync(x => x.Id == chosen.Id)
      ).TrailerId
    );

    // An accepted execution leg answers alone for its truck.
    var executing = await TruckAsync(f, "11007");
    await LoadAsync(f, executing, "in_transit", "77777");
    var legTrailer = new Trailer
    {
      Id = Guid.NewGuid(),
      UnitNumber = "88888",
      IsActive = true,
    };
    var trip = new Trip { Id = Guid.NewGuid(), Status = "active" };
    f.Db.AddRange(
      legTrailer,
      trip,
      new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        TruckId = executing.Id,
        TrailerId = legTrailer.Id,
        Status = "active",
      }
    );
    await f.Db.SaveChangesAsync();
    await ResolveAsync(f);
    var row = await f
      .Db.Trucks.AsNoTracking()
      .SingleAsync(x => x.Id == executing.Id);
    Assert.Equal(
      (legTrailer.Id, "execution"),
      (row.TrailerId, row.TrailerSource)
    );

    // A trailer made inactive here is unavailable whoever names it.
    await f
      .Db.Trailers.Where(x => x.Id == legTrailer.Id)
      .ExecuteUpdateAsync(x => x.SetProperty(t => t.IsActive, false));
    await ResolveAsync(f);
    row = await f
      .Db.Trucks.AsNoTracking()
      .SingleAsync(x => x.Id == executing.Id);
    Assert.Equal((null, legTrailer.Id), (row.TrailerId, row.TrailerConflictId));
  }

  [Fact]
  public async Task OneTrailerIsNeverSilentlyTakenFromAnotherTruck()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var byLoad = await TruckAsync(f, "11005");
    var byTelemetry = await TruckAsync(f, "11006", driver: "d-2");
    await TrailerCatalog.ApplyAsync(
      f.Db,
      Source,
      [Reported("t-1", "55904")],
      default
    );
    await f.Db.SaveChangesAsync();
    await LoadAsync(f, byLoad, "in_transit", "55904");
    await SyncAsync(f, ("d-2", "t-1", null));

    var trucks = await f.Db.Trucks.AsNoTracking().ToDictionaryAsync(x => x.Id);
    var trailer = (await f.Db.Trailers.SingleAsync()).Id;
    Assert.Equal(trailer, trucks[byTelemetry.Id].TrailerId);
    Assert.Equal(
      (null, trailer),
      (trucks[byLoad.Id].TrailerId, trucks[byLoad.Id].TrailerConflictId)
    );

    // Two loads name it for two trucks: neither takes it.
    var other = await TruckAsync(f, "11007");
    await SyncAsync(f);
    await LoadAsync(f, other, "in_transit", "55904");
    await ResolveAsync(f);
    Assert.All(
      await f
        .Db.Trucks.AsNoTracking()
        .Where(x => x.Id != byTelemetry.Id)
        .ToListAsync(),
      x => Assert.Equal((null, trailer), (x.TrailerId, x.TrailerConflictId))
    );
  }

  [Fact]
  public async Task AnotherCarriersLoadsAndTrucksAreNotConsulted()
  {
    await using var f = await PlanningRefreshFixture.CreateAsync();
    var mine = await TruckAsync(f, "11005");
    await using (var scope = f.NewScope())
    using (
      scope
        .ServiceProvider.GetRequiredService<ICurrentCompany>()
        .As(Guid.NewGuid())
    )
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var theirs = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "x",
        UnitNumber = "11005",
        IsActive = true,
      };
      db.Add(theirs);
      db.Add(Load(theirs.Id, "in_transit", "55904", null, 0));
      await db.SaveChangesAsync();
    }

    await ResolveAsync(f);

    Assert.Empty(await f.Db.Trailers.ToListAsync());
    Assert.Null((await f.Db.Trucks.AsNoTracking().SingleAsync()).TrailerId);
  }

  [Fact]
  public async Task TheFleetIsResolvedInAFixedNumberOfQueries()
  {
    var counter = new CommandCounter();
    await using var f = await PlanningRefreshFixture.CreateAsync(services =>
      services.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(counter))
    );
    async Task<int> CountAsync()
    {
      f.Db.ChangeTracker.Clear();
      counter.Reads = 0;
      await TruckTrailerAssignments.ResolveAsync(f.Db, default);
      return counter.Reads;
    }
    var first = await TruckAsync(f, "1");
    await LoadAsync(f, first, "in_transit", "55904");
    await ResolveAsync(f);
    var few = await CountAsync();
    for (var i = 2; i <= 20; i++)
      await LoadAsync(
        f,
        await TruckAsync(f, i.ToString()),
        "in_transit",
        (60000 + i).ToString()
      );
    await ResolveAsync(f);
    Assert.Equal(few, await CountAsync());
  }

  private static async Task<IReadOnlyCollection<Guid>> ResolveAsync(
    PlanningRefreshFixture f
  )
  {
    f.Db.ChangeTracker.Clear();
    var changed = await TruckTrailerAssignments.ResolveAsync(f.Db, default);
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
    return changed;
  }

  // One provider answer: driver d-1 or d-2 on their truck, and the
  // driver-trailer records given.
  private static async Task SyncAsync(
    PlanningRefreshFixture f,
    params (string Driver, string Trailer, DateTime? Ended)[] trailers
  )
  {
    f.Db.ChangeTracker.Clear();
    var now = DateTime.UtcNow;
    var trucks = await f
      .Db.Trucks.AsNoTracking()
      .Include(x => x.Driver)
      .ToListAsync();
    await FleetAssignmentSync.SyncAsync(
      f.Db,
      trucks
        .Where(x => x.Driver is not null)
        .Select(x => new ExternalFleetAssignment
        {
          DriverExternalId = x.Driver!.ExternalId,
          VehicleExternalId = x.ExternalId,
          StartTime = now.AddDays(-1),
        })
        .ToList(),
      trailers
        .Select(x => new ExternalTrailerAssignment
        {
          DriverExternalId = x.Driver,
          TrailerExternalId = x.Trailer,
          StartTime = now.AddDays(-1),
          EndTime = x.Ended,
        })
        .ToList(),
      now,
      Source
    );
    await f.Db.SaveChangesAsync();
    f.Db.ChangeTracker.Clear();
  }

  private static async Task<Truck> TruckAsync(
    PlanningRefreshFixture f,
    string unit,
    string? driver = null
  )
  {
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "v-" + unit,
      UnitNumber = unit,
      IsActive = true,
    };
    if (driver is not null)
      f.Db.Drivers.Add(
        new Driver
        {
          Id = Guid.NewGuid(),
          ExternalId = driver,
          Name = driver,
          IsActive = true,
        }
      );
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    if (driver is not null)
    {
      truck.DriverId = (
        await f.Db.Drivers.SingleAsync(x => x.ExternalId == driver)
      ).Id;
      await f.Db.SaveChangesAsync();
    }
    return truck;
  }

  private static async Task<Load> LoadAsync(
    PlanningRefreshFixture f,
    Truck truck,
    string status,
    string trailer,
    string? stopTrailer = null,
    int completedStops = 0,
    string? provider = null
  )
  {
    var load = Load(truck.Id, status, trailer, stopTrailer, completedStops);
    f.Db.Dispatches.Add(load);
    if (provider is not null)
      f.Db.DispatchSourceLinks.Add(
        new DispatchSourceLink
        {
          Provider = provider,
          ExternalId = load.Id.ToString("N"),
          DispatchId = load.Id,
          Dispatch = load,
        }
      );
    await f.Db.SaveChangesAsync();
    return load;
  }

  private static Load Load(
    Guid truck,
    string status,
    string trailer,
    string? stopTrailer,
    int completedStops
  )
  {
    var id = Guid.NewGuid();
    return new Load
    {
      Id = id,
      LoadNumber = Random.Shared.Next(1000, 999999),
      Status = status,
      TruckId = truck,
      TrailerNumber = trailer,
      Stops = Enumerable
        .Range(0, 2)
        .Select(i => new DispatchStop
        {
          Id = Guid.NewGuid(),
          DispatchId = id,
          Sequence = i + 1,
          Job = i == 0 ? "Pick Up" : "Drop Off",
          TrailerNumber =
            i == 1 && stopTrailer is not null ? stopTrailer : trailer,
          DepartedAt = i < completedStops ? DateTime.UtcNow : null,
        })
        .ToList(),
    };
  }

  private static ExternalTrailer Reported(string id, string unit) =>
    new()
    {
      ExternalId = id,
      UnitNumber = unit,
      IsActive = true,
    };

  private sealed class CommandCounter : DbCommandInterceptor
  {
    public int Reads { get; set; }

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      Reads++;
      return ValueTask.FromResult(result);
    }
  }
}
