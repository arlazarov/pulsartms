using Application.Features.Fuel.Background;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Options;
using Application.Features.Fuel.Services;
using Application.Interfaces;
using Domain.Entities.Fuel;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Fuel;

// The import asks the place provider about a station only when its name or
// location changed, so a station that quietly shut is never asked about
// again - and that is the station a driver is sent to a locked gate for.
// This asks on a schedule instead, spending a bounded number of billed
// questions per pass.
[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelStationStatusOperationTests
{
  [Fact]
  public async Task AStationNobodyHasAskedAboutIsAskedFirst()
  {
    await using var f = await Fixture.CreateAsync();
    await f.AddAsync("old", checkedAt: f.Now.AddDays(-365));
    await f.AddAsync("never", checkedAt: null);
    f.Places.Status = FuelStationStatus.Operational;
    f.Options.BatchSize = 1;

    Assert.Equal(1, await f.Operation.RunOnceAsync(default));

    Assert.Equal([f.Query("never")], f.Places.Asked);
  }

  [Fact]
  public async Task AClosedStationIsRecordedAsClosed()
  {
    await using var f = await Fixture.CreateAsync();
    await f.AddAsync("shut", checkedAt: null);
    f.Places.Status = FuelStationStatus.ClosedPermanently;

    await f.Operation.RunOnceAsync(default);

    var station = await f.Read("shut");
    Assert.Equal(FuelStationStatus.ClosedPermanently, station.BusinessStatus);
    Assert.NotNull(station.StatusCheckedAt);
  }

  // Each question is billed, so a backlog has to drain at a stated rate
  // rather than arrive as one bill.
  [Fact]
  public async Task NoMoreThanTheBudgetIsAskedInOnePass()
  {
    await using var f = await Fixture.CreateAsync();
    for (var i = 0; i < 7; i++)
      await f.AddAsync($"s{i}", checkedAt: null);
    f.Options.BatchSize = 3;

    Assert.Equal(3, await f.Operation.RunOnceAsync(default));

    Assert.Equal(3, f.Places.Asked.Count);
  }

  // Asking again about an answer that is still fresh spends the budget the
  // unasked stations need.
  [Fact]
  public async Task AFreshAnswerIsNotPaidForAgain()
  {
    await using var f = await Fixture.CreateAsync();
    await f.AddAsync("fresh", checkedAt: f.Now.AddDays(-1));
    f.Options.RecheckDays = 14;

    Assert.Equal(0, await f.Operation.RunOnceAsync(default));

    Assert.Empty(f.Places.Asked);
  }

  // A station with no current price is nowhere anybody is being sent today.
  [Fact]
  public async Task AStationWithoutCurrentPricesIsNotAskedAbout()
  {
    await using var f = await Fixture.CreateAsync();
    await f.AddAsync("stale-price", checkedAt: null, pricedToday: false);

    Assert.Equal(0, await f.Operation.RunOnceAsync(default));

    Assert.Empty(f.Places.Asked);
  }

  // The provider answering nothing is not the provider saying "shut", but
  // the attempt still has to be recorded or this station is asked about on
  // every pass and no other station is ever reached.
  [Fact]
  public async Task AnUnansweredLookupDoesNotReadAsClosedAndStillAdvances()
  {
    await using var f = await Fixture.CreateAsync();
    await f.AddAsync("silent", checkedAt: null);
    f.Places.Result = null;

    await f.Operation.RunOnceAsync(default);

    var station = await f.Read("silent");
    Assert.Equal("", station.BusinessStatus);
    Assert.NotNull(station.StatusCheckedAt);
  }

  private sealed class PlaceStub : IPlaceSearchService
  {
    public List<string> Asked { get; } = [];
    public string Status { get; set; } = FuelStationStatus.Operational;
    public PlaceSearchResult? Result { get; set; } = new();

    public Task<PlaceSearchResult?> SearchAsync(
      string query,
      CancellationToken ct = default
    )
    {
      Asked.Add(query);
      return Task.FromResult(
        Result is null
          ? null
          : new PlaceSearchResult
          {
            PlaceId = "p",
            Latitude = 43,
            Longitude = -79,
            BusinessStatus = Status,
          }
      );
    }
  }

  private sealed class StoreStub : IFuelStationLookupStore
  {
    private readonly Dictionary<string, FuelStationLookupState> states = [];

    public Task<FuelStationLookupState?> ReadAsync(
      string id,
      CancellationToken ct
    ) => Task.FromResult(states.GetValueOrDefault(id));

    public Task<bool> IsCurrentAsync(
      string id,
      string revision,
      CancellationToken ct
    ) => Task.FromResult(true);

    public Task<bool> AcquireAsync(
      string id,
      string owner,
      DateTime now,
      CancellationToken ct
    ) => Task.FromResult(true);

    public Task SaveAsync(
      string id,
      string owner,
      FuelStationLookupState state,
      CancellationToken ct
    )
    {
      states[id] = state;
      return Task.CompletedTask;
    }

    public Task ReleaseAsync(string id, string owner, CancellationToken ct) =>
      Task.CompletedTask;
  }

  private sealed class Fixture : IAsyncDisposable
  {
    private SqliteConnection Connection { get; init; } = null!;
    private ServiceProvider Services { get; init; } = null!;
    public required AppDbContext Db { get; init; }
    public required PlaceStub Places { get; init; }
    public required FuelStationStatusOptions Options { get; init; }
    public required FuelStationStatusOperation Operation { get; init; }
    public required ManualTimeProvider Clock { get; init; }
    public DateTime Now => Clock.UtcNow.UtcDateTime;

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      var clock = new ManualTimeProvider();
      var places = new PlaceStub();
      var lookups = new FuelStationLookupService(
        new StoreStub(),
        places,
        clock
      );
      var services = new ServiceCollection()
        .AddSingleton<IAppDbContext>(db)
        .AddSingleton(lookups)
        .BuildServiceProvider();
      var options = new FuelStationStatusOptions();
      return new Fixture
      {
        Connection = connection,
        Services = services,
        Db = db,
        Places = places,
        Options = options,
        Clock = clock,
        Operation = new FuelStationStatusOperation(
          services.GetRequiredService<IServiceScopeFactory>(),
          Microsoft.Extensions.Options.Options.Create(options),
          clock,
          NullLogger<FuelStationStatusOperation>.Instance
        ),
      };
    }

    public string Query(string id) => $"{id}, City, ON";

    public async Task AddAsync(
      string id,
      DateTime? checkedAt,
      bool pricedToday = true
    )
    {
      var today = DateOnly.FromDateTime(Now);
      Db.FuelStations.Add(
        new FuelStation
        {
          Id = Guid.NewGuid(),
          ExternalId = id,
          Name = id,
          City = "City",
          Region = "ON",
          Latitude = 43,
          Longitude = -79,
          StatusCheckedAt = checkedAt,
          FuelDiscounts =
          [
            new FuelDiscount
            {
              Id = Guid.NewGuid(),
              Currency = "CAD",
              Product = "Diesel",
              RetailPrice = 2,
              DiscountPrice = 1.8m,
              EffectiveFrom = pricedToday ? today : today.AddDays(-30),
              EffectiveTo = pricedToday ? today : today.AddDays(-20),
            },
          ],
        }
      );
      await Db.SaveChangesAsync();
      Db.ChangeTracker.Clear();
    }

    public async Task<FuelStation> Read(string id)
    {
      Db.ChangeTracker.Clear();
      return await Db
        .FuelStations.AsNoTracking()
        .SingleAsync(x => x.ExternalId == id);
    }

    public async ValueTask DisposeAsync()
    {
      await Db.DisposeAsync();
      await Services.DisposeAsync();
      await Connection.DisposeAsync();
    }
  }
}
