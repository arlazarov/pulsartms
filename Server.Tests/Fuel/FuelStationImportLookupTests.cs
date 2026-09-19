using Application.Caching;
using Application.Features.Fuel.Commands.ImportFuelDiscounts;
using Application.Features.Fuel.Interfaces;
using Application.Features.Fuel.Models;
using Application.Features.Fuel.Services;
using Infrastructure.Integrations.Google.Places;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelStationImportLookupTests
{
  [Fact]
  public async Task MissingProvinceCannotOverwriteExistingStationOrTriggerLookup()
  {
    await using var fixture = await Fixture.Create();
    fixture.Db.FuelStations.Add(
      new()
      {
        Id = Guid.NewGuid(),
        ExternalId = "station",
        Name = "Station",
        Region = "ON",
        City = "Toronto",
        Address = "Original address",
        Latitude = 43,
        Longitude = -79,
      }
    );
    await fixture.Db.SaveChangesAsync();
    var places = new Places();
    var lookups = new FuelStationLookupService(
      fixture.Store,
      places,
      TimeProvider.System
    );
    var row = new FuelDiscountImportRow
    {
      StationId = "station",
      Name = "Station",
      City = "Toronto",
    };
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => FuelStationSync.PrepareAsync(fixture.Db, lookups, [row])
    );
    await Assert.ThrowsAsync<InvalidOperationException>(
      () =>
        FuelStationSync.SyncAsync(
          fixture.Db,
          lookups,
          new Dictionary<(string, string), FuelStationLookupResult>(),
          [row]
        )
    );
    Assert.Equal(0, places.Calls);
    var station = await fixture.Db.FuelStations.SingleAsync();
    Assert.Equal("ON", station.Region);
    Assert.Equal("Original address", station.Address);
    Assert.Equal(43, station.Latitude);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task LatestPreparedRevisionWinsRegardlessOfImportApplicationOrder(
    bool correctedFirst
  )
  {
    await using var fixture = await Fixture.Create();
    var places = new Places
    {
      Response = query =>
        Task.FromResult<PlaceSearchResult?>(
          new()
          {
            Address = query,
            Latitude = query.Contains("New Town") ? 41 : 40,
            Longitude = -80,
          }
        ),
    };
    var lookups = new FuelStationLookupService(
      fixture.Store,
      places,
      TimeProvider.System
    );
    var old = Import("old", "Station", "Old Town");
    old.EffectiveDate = old.EffectiveDate.AddDays(-1);
    var corrected = Import("corrected", "Station", "New Town");
    var oldPrepared = await FuelStationSync.PrepareAsync(
      fixture.Db,
      lookups,
      old.Rows
    );
    var correctedPrepared = await FuelStationSync.PrepareAsync(
      fixture.Db,
      lookups,
      corrected.Rows
    );
    var sequence = correctedFirst
      ? new[] { (corrected, correctedPrepared), (old, oldPrepared) }
      : new[] { (old, oldPrepared), (corrected, correctedPrepared) };
    foreach (var (import, prepared) in sequence)
    {
      await using var transaction =
        await fixture.Db.Database.BeginTransactionAsync();
      await FuelStationSync.SyncAsync(
        fixture.Db,
        lookups,
        prepared,
        import.Rows
      );
      await fixture.Db.SaveChangesAsync();
      await FuelDiscountSync.SyncAsync(fixture.Db, import, default);
      await fixture.Db.SaveChangesAsync();
      await transaction.CommitAsync();
    }
    var station = await fixture.Db.FuelStations.SingleAsync();
    Assert.Equal("New Town", station.City);
    Assert.Equal(41, station.Latitude);
    Assert.Equal(2, await fixture.Db.FuelDiscounts.CountAsync());
    Assert.Equal(2, places.Calls);
  }

  [Fact]
  public async Task EveryLookupIsPreparedAndReservedBeforeTheImportTransaction()
  {
    await using var fixture = await Fixture.Create();
    var places = new Places
    {
      Response = async query =>
      {
        Assert.Null(fixture.Db.Database.CurrentTransaction);
        var state = await fixture.Store.ReadAsync(
          query.StartsWith("Second") ? "second" : "station",
          default
        );
        Assert.True(state!.Pending);
        Assert.True(state.NextAttemptAt > DateTime.UtcNow);
        return new()
        {
          Address = query,
          Latitude = 40,
          Longitude = -80,
        };
      },
    };
    var first = Import("email", "Station", "Town");
    var second = Import("email", "Second", "Town", "second");
    var handler = fixture.Handler(places, first, second);
    Assert.True((await handler.Handle(new(), default)).Success);
    Assert.Equal(2, places.Calls);
    Assert.Equal(2, await fixture.Db.FuelStations.CountAsync());
    Assert.Equal(2, await fixture.Db.FuelDiscounts.CountAsync());
    Assert.Equal(1, await fixture.Db.FuelImportSources.CountAsync());
    Assert.True((await handler.Handle(new(), default)).Success);
    Assert.Equal(2, places.Calls);
  }

  [Fact]
  public async Task BusyCorrectedQueryDoesNotConsumeEmailAndRetriesAfterOldValidResult()
  {
    await using var fixture = await Fixture.Create();
    var entered = new TaskCompletionSource(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var oldResult = new TaskCompletionSource<PlaceSearchResult?>(
      TaskCreationOptions.RunContinuationsAsynchronously
    );
    var places = new Places
    {
      Response = _ =>
      {
        entered.SetResult();
        return oldResult.Task;
      },
    };
    var oldLookup = new FuelStationLookupService(
      fixture.Store,
      places,
      TimeProvider.System
    ).FindAsync("station", "Station, Old Town, TX", default);
    await entered.Task;
    var corrected = fixture.Handler(
      places,
      Import("corrected-email", "Station", "New Town")
    );
    var deferred = await corrected.Handle(new(), default);
    Assert.False(deferred.Success);
    Assert.Equal(503, deferred.StatusCode);
    Assert.Equal(0, await fixture.Db.FuelImportSources.CountAsync());
    Assert.Equal(0, await fixture.Db.FuelStations.CountAsync());
    Assert.Equal(1, places.Calls);
    oldResult.SetResult(
      new()
      {
        Address = "Old address",
        Latitude = 40,
        Longitude = -80,
      }
    );
    await oldLookup;
    Assert.True(
      (
        await fixture
          .Handler(places, Import("old-email", "Station", "Old Town"))
          .Handle(new(), default)
      ).Success
    );
    places.Response = _ =>
      Task.FromResult<PlaceSearchResult?>(
        new()
        {
          Address = "Corrected address",
          Latitude = 41,
          Longitude = -79,
        }
      );
    Assert.True((await corrected.Handle(new(), default)).Success);
    Assert.Equal(2, places.Calls);
    Assert.Equal(2, await fixture.Db.FuelImportSources.CountAsync());
    var station = await fixture.Db.FuelStations.SingleAsync();
    Assert.Equal("New Town", station.City);
    Assert.Equal("Corrected address", station.Address);
    Assert.Equal(41, station.Latitude);
  }

  private static FuelDiscountImportData Import(
    string message,
    string name,
    string city,
    string station = "station"
  ) =>
    new()
    {
      MessageId = message,
      AttachmentName = station + ".csv",
      Currency = "USD",
      EffectiveDate = new(2026, 9, 8),
      Rows =
      [
        new()
        {
          StationId = station,
          Name = name,
          City = city,
          State = "TX",
          RetailPrice = 4,
          DiscountPrice = 3,
        },
      ],
    };

  private sealed class Fixture(
    SqliteConnection connection,
    ServiceProvider provider,
    AsyncServiceScope scope
  ) : IAsyncDisposable
  {
    public AppDbContext Db { get; } =
      scope.ServiceProvider.GetRequiredService<AppDbContext>();
    public FuelStationLookupStore Store { get; } =
      new(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(),
        provider.GetRequiredService<IServiceScopeFactory>()
      );
    private readonly ReadCache reads = TestCache.Create();

    public ImportFuelDiscountsHandler Handler(
      Places places,
      params FuelDiscountImportData[] imports
    ) =>
      new(
        Db,
        new Provider(imports),
        new(Store, places, TimeProvider.System),
        reads
      );

    public static async Task<Fixture> Create()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var services = new ServiceCollection();
      services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(connection)
      );
      var provider = services.BuildServiceProvider();
      var fixture = new Fixture(
        connection,
        provider,
        provider.CreateAsyncScope()
      );
      await fixture.Db.Database.EnsureCreatedAsync();
      return fixture;
    }

    public async ValueTask DisposeAsync()
    {
      reads.Dispose();
      await scope.DisposeAsync();
      await provider.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class Provider(IReadOnlyList<FuelDiscountImportData> imports)
    : IFuelDiscountProvider
  {
    public Task<IReadOnlyList<FuelDiscountImportData>> GetDiscountsAsync(
      IReadOnlyCollection<string> ids,
      CancellationToken ct = default
    ) => Task.FromResult(imports);
  }

  private sealed class Places : IPlaceSearchService
  {
    // Reading by id is not what these exercise; they place stations by name.
    public Task<PlaceSearchResult?> ReadAsync(
      string placeId,
      CancellationToken cancellationToken = default
    ) => Task.FromResult<PlaceSearchResult?>(null);

    public int Calls;
    public Func<string, Task<PlaceSearchResult?>> Response = _ =>
      Task.FromResult<PlaceSearchResult?>(null);

    public Task<PlaceSearchResult?> SearchAsync(
      string query,
      CancellationToken ct = default
    )
    {
      Calls++;
      return Response(query);
    }
  }
}
