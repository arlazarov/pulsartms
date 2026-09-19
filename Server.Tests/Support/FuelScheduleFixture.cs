using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Eta.Options;
using Application.Features.Eta.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Server.Tests.Support;

internal sealed class FuelScheduleFixture(
  SqliteConnection connection,
  AppDbContext db,
  EtaMemory memory,
  FuelScheduleFixture.HosInputs hos,
  FuelScheduleEvaluator evaluator,
  Guid truckId
) : IAsyncDisposable
{
  public AppDbContext Db => db;
  public EtaMemory Memory => memory;
  public HosInputs Hos => hos;
  public FuelScheduleEvaluator Evaluator => evaluator;
  public Guid TruckId => truckId;

  public static async Task<FuelScheduleFixture> CreateAsync(
    DateTimeOffset now,
    double cycle = 50,
    double drive = 10,
    double firstDayHours = 0
  )
  {
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options
    );
    await db.Database.EnsureCreatedAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "preview-truck",
      UnitNumber = "preview",
      Driver = new Driver
      {
        Id = Guid.NewGuid(),
        ExternalId = "preview-driver",
      },
    };
    db.Trucks.Add(truck);
    await db.SaveChangesAsync();
    var history = HosForecastFixture.History(
      now,
      cycleHours: cycle,
      firstDayHours: firstDayHours
    );
    var hos = new HosInputs(
      HosForecastFixture.Clocks(now, history, drive),
      history
    );
    var memory = new EtaMemory();
    var eta = new EtaService(
      db,
      hos,
      new Regions(),
      memory,
      hos,
      Options.Create(new EtaPlanningOptions())
    );
    return new(connection, db, memory, hos, new(db, hos, hos, eta), truck.Id);
  }

  public async ValueTask DisposeAsync()
  {
    memory.Dispose();
    await db.DisposeAsync();
    await connection.DisposeAsync();
  }

  internal sealed class HosInputs(DriverHosClocks clocks, HosHistory history)
    : IDriverHosProvider,
      IHosHistoryProvider
  {
    public DriverHosClocks? Clocks { get; set; } = clocks;
    public HosHistory? History { get; set; } = history;
    public Exception? ClockFailure { get; set; }
    public Exception? HistoryFailure { get; set; }
    public int ClockCalls { get; private set; }
    public int HistoryCalls { get; private set; }

    public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
      CancellationToken ct
    )
    {
      ClockCalls++;
      if (ClockFailure is { } failure)
        throw failure;
      return Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(
        Clocks is { } value
          ? new Dictionary<string, DriverHosClocks>
          {
            ["preview-driver"] = value,
          }
          : new Dictionary<string, DriverHosClocks>()
      );
    }

    public Task<HosHistory?> GetAsync(string driverId, CancellationToken ct)
    {
      HistoryCalls++;
      if (HistoryFailure is { } failure)
        throw failure;
      Assert.Equal("preview-driver", driverId);
      return Task.FromResult(History);
    }
  }

  private sealed class Regions : IRouteRegionLookup
  {
    public RouteRegion Find(RoutePoint point) => new("US", "Etc/UTC", false);
  }
}
