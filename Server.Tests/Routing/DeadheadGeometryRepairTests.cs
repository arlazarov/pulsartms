using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Finance")]
[Trait("Kind", "Integration")]
public sealed partial class DeadheadGeometryRepairTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ConcurrentCompletionKeepsTheWinnerWithoutResavingTheLoser(
    bool providerFails
  )
  {
    await using var fixture = await Fixture.CreateAsync(null);
    var calculatedAt = DateTime.UtcNow;
    fixture.Router.Read = async (points, ct) =>
    {
      await using var competing = fixture.CreateContext();
      var saved = await competing.DispatchDeadheads.SingleAsync(ct);
      var route = Complete(points);
      route.Miles = 140;
      route.Legs = [new(140, route.Seconds, points.ToList())];
      saved.Miles = 140;
      saved.RouteJson = RoutePlanStorage.Serialize(route);
      saved.CalculatedAt = calculatedAt;
      saved.RetryAfter = DateTime.MinValue;
      saved.ErrorMessage = null;
      await competing.SaveChangesAsync(ct);
      await new DispatchRates(competing).SaveAsync(
        new(
          fixture.Load.Id,
          fixture.Load.Price,
          fixture.Load.LoadedMiles,
          fixture.Load.Currency
        ),
        saved.Miles,
        saved.InputHash,
        ct
      );
      if (providerFails)
        throw new RoutePlanningException("The losing request failed.");
      return Complete(points);
    };

    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );

    Assert.Empty(fixture.Db.ChangeTracker.Entries<DispatchDeadhead>());
    await fixture.Db.SaveChangesAsync();
    var winner = await fixture
      .Db.DispatchDeadheads.AsNoTracking()
      .SingleAsync();
    Assert.Equal(140m, winner.Miles);
    Assert.Equal(calculatedAt, winner.CalculatedAt);
    Assert.Equal(DateTime.MinValue, winner.RetryAfter);
    Assert.Null(winner.ErrorMessage);
    Assert.Equal(140, SavedRouteReader.Route(winner.RouteJson, 1)!.Miles);
    Assert.Equal(
      140m,
      (await fixture.Db.DispatchRates.AsNoTracking().SingleAsync()).EmptyMiles
    );
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.Equal(1, fixture.Router.Calls);
  }

  [Fact]
  public async Task AddressReadinessReleasesPermanentRetryWithoutManualReset()
  {
    await using var fixture = await Fixture.CreateAsync(null);
    var previous = await fixture
      .Db.Dispatches.Include(load => load.Stops)
      .SingleAsync(load => load.Id != fixture.Load.Id);
    var stop = previous.Stops.OrderBy(value => value.Sequence).Last();
    stop.Address = "410 Warehouse Road";
    stop.City = "Facility City";
    stop.SourceAddressJson = StopAddress.From(stop).Serialize();
    var saved = await fixture.Db.DispatchDeadheads.SingleAsync();
    saved.InputHash = DeadheadConnection
      .Find(fixture.Load, [previous])!
      .Signature(fixture.Profile);
    saved.Miles = null;
    await fixture.Db.SaveChangesAsync();
    fixture.Router.Geocode = (_, _) =>
      throw new RoutePlanningException("The street needs confirmation.");

    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Services.Deadheads.EnsureAsync(
          fixture.Load,
          fixture.Profile,
          default
        )
    );
    Assert.Equal(DateTime.MaxValue, saved.RetryAfter);
    var failedHash = saved.InputHash;
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.Equal(1, fixture.Router.GeocodeCalls);
    Assert.Equal(0, fixture.Router.Calls);

    stop.AddressVerifiedAt = DateTime.UtcNow;
    await fixture.Db.SaveChangesAsync();
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.NotEqual(failedHash, saved.InputHash);
    Assert.Equal(DateTime.MinValue, saved.RetryAfter);
    Assert.Null(saved.ErrorMessage);
    Assert.NotNull(SavedRouteReader.Route(saved.RouteJson, 1));
    Assert.Equal(1, fixture.Router.GeocodeCalls);
    Assert.Equal(1, fixture.Router.Calls);

    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.Equal(1, fixture.Router.Calls);
  }

  [Fact]
  public async Task MatchingInputHashCannotReuseAConnectionThatEndsAtTheWrongFacility()
  {
    await using var fixture = await Fixture.CreateAsync(null);
    var previous = await fixture
      .Db.Dispatches.Include(load => load.Stops)
      .SingleAsync(load => load.Id != fixture.Load.Id);
    var row = await fixture.Db.DispatchDeadheads.SingleAsync();
    row.RouteJson = JsonSerializer.Serialize(
      Complete([new(41, -79), new(40 + 2.61 / 69, -80)]),
      RoutingJson.Options
    );
    await fixture.Db.SaveChangesAsync();
    Assert.Null(
      await fixture.Services.Deadheads.ReadRouteAsync(
        previous.Id,
        fixture.Load,
        fixture.Profile,
        default
      )
    );
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    var repaired = await fixture.Services.Deadheads.ReadRouteAsync(
      previous.Id,
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.True(RouteAnchoring.Matches(repaired, [new(41, -79), new(40, -80)]));
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.Equal(1, fixture.Router.Calls);
    using var json = JsonDocument.Parse(
      (
        await fixture.Db.DispatchDeadheads.AsNoTracking().SingleAsync()
      ).RouteJson!
    );
    Assert.False(json.RootElement.TryGetProperty("points", out _));
  }

  [Fact]
  public async Task FreshMisanchoredConnectionFailsBeforePublishingGeometryOrNewMileage()
  {
    await using var fixture = await Fixture.CreateAsync(null);
    fixture.Router.Read = (points, _) =>
      Task.FromResult(
        Complete(
          [
            points[0],
            new(points[^1].Latitude + 2.61 / 69, points[^1].Longitude),
          ]
        )
      );
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Services.Deadheads.EnsureAsync(
          fixture.Load,
          fixture.Profile,
          default
        )
    );
    var saved = await fixture.Db.DispatchDeadheads.AsNoTracking().SingleAsync();
    Assert.Equal(80m, saved.Miles);
    Assert.Null(saved.RouteJson);
    Assert.Equal(1, fixture.Router.Calls);
    Assert.Equal(
      "The empty route does not reach the confirmed stops.",
      saved.ErrorMessage
    );
  }

  [Fact]
  public async Task OverlappingAppointmentsStillPrepareAndPublishEmptyMileageOnce()
  {
    await using var fixture = await Fixture.CreateAsync(null);
    var previous = await fixture
      .Db.Dispatches.Include(x => x.Stops)
      .SingleAsync(x => x.Id != fixture.Load.Id);
    previous.Stops[^1].ScheduledDate = new(2026, 9, 6);
    await fixture.Db.SaveChangesAsync();

    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    var card = new DispatchResponse
    {
      Id = fixture.Load.Id,
      TruckId = fixture.Load.TruckId,
      Price = 1000,
      LoadedMiles = 400,
      Currency = "USD",
    };
    await fixture.Services.Deadheads.ReadAsync([card], default);
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );

    Assert.Equal(100m, card.EmptyMiles);
    Assert.Equal("ready", card.EmptyMilesStatus);
    Assert.Equal(2m, card.TotalRatePerMile);
    Assert.Equal(1, fixture.Router.Calls);
    var route = await fixture.Services.Deadheads.ReadRouteAsync(
      previous.Id,
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.Equal(3600, route!.Seconds);
  }

  [Theory]
  [InlineData(null)]
  [InlineData("{")]
  [InlineData("{\"legs\":null}")]
  public async Task GeometryRepairKeepsFinancialMileageUntilSuccessAndRunsOnlyOnce(
    string? geometry
  )
  {
    await using var fixture = await Fixture.CreateAsync(geometry);
    fixture.Router.Read = async (points, _) =>
    {
      var pending = await fixture
        .Db.DispatchDeadheads.AsNoTracking()
        .SingleAsync();
      Assert.Equal(80m, pending.Miles);
      Assert.True(pending.RetryAfter > DateTime.UtcNow);
      Assert.Null(pending.RouteJson);
      var rate = await fixture.Db.DispatchRates.AsNoTracking().SingleAsync();
      Assert.Equal(1000m / 480, rate.TotalRatePerMile!.Value, 3);
      return Complete(points);
    };
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    var saved = await fixture.Db.DispatchDeadheads.AsNoTracking().SingleAsync();
    Assert.Equal(100m, saved.Miles);
    Assert.NotNull(SavedRouteReader.Route(saved.RouteJson, 1));
    Assert.Equal(DateTime.MinValue, saved.RetryAfter);
    Assert.Null(saved.ErrorMessage);
    Assert.Equal(
      2m,
      (
        await fixture.Db.DispatchRates.AsNoTracking().SingleAsync()
      ).TotalRatePerMile
    );
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.Equal(1, fixture.Router.Calls);
  }

  [Theory]
  [InlineData("invalid")]
  [InlineData("http")]
  [InlineData("cancelled")]
  public async Task FailedGeometryRepairRetainsMileageAndThePersistedRetryBudget(
    string failure
  )
  {
    await using var fixture = await Fixture.CreateAsync(null);
    using var cancellation = new CancellationTokenSource();
    fixture.Router.Read = (_, _) =>
    {
      if (failure == "http")
        throw new HttpRequestException("Synthetic provider failure");
      if (failure == "cancelled")
      {
        cancellation.Cancel();
        return Task.FromCanceled<TruckRoute>(cancellation.Token);
      }
      return Task.FromResult(new TruckRoute { Miles = 100 });
    };
    Task Repair() =>
      fixture.Services.Deadheads.EnsureAsync(
        fixture.Load,
        fixture.Profile,
        cancellation.Token
      );
    if (failure == "http")
      await Assert.ThrowsAsync<HttpRequestException>(Repair);
    else if (failure == "cancelled")
      await Assert.ThrowsAnyAsync<OperationCanceledException>(Repair);
    else
      await Assert.ThrowsAsync<RoutePlanningException>(Repair);
    var saved = await fixture.Db.DispatchDeadheads.AsNoTracking().SingleAsync();
    Assert.Equal(80m, saved.Miles);
    Assert.Null(saved.RouteJson);
    Assert.InRange(
      saved.RetryAfter,
      DateTime.UtcNow,
      DateTime.UtcNow.AddMinutes(6)
    );
    if (failure == "invalid")
      Assert.Equal("Empty route geometry is incomplete.", saved.ErrorMessage);
    await fixture.Services.Deadheads.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default
    );
    Assert.Equal(1, fixture.Router.Calls);
  }

  private static TruckRoute Complete(IReadOnlyList<RoutePoint> points) =>
    new()
    {
      Miles = 100,
      Seconds = 3600,
      Points = points.ToList(),
      Legs = [new(100, 3600, points.ToList())],
    };

  private sealed class Fixture(
    SqliteConnection connection,
    AppDbContext db,
    Load load,
    PlanningTestServices services,
    TruckRouteProfile profile,
    Router router,
    PublicationProbe publication
  ) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public Load Load => load;
    public PlanningTestServices Services => services;
    public TruckRouteProfile Profile => profile;
    public Router Router => router;
    public PublicationProbe Publication => publication;

    public AppDbContext CreateContext() =>
      new(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      );

    public static async Task<Fixture> CreateAsync(
      string? geometry,
      params IInterceptor[] interceptors
    )
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .AddInterceptors(interceptors)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "repair",
        UnitNumber = "repair",
      };
      db.Trucks.Add(truck);
      db.Dispatches.AddRange(CreateLoad(truck.Id, 1), CreateLoad(truck.Id, 5));
      await db.SaveChangesAsync();
      var loads = await db
        .Dispatches.AsNoTracking()
        .Include(x => x.Stops)
        .OrderBy(x => x.LoadNumber)
        .ToListAsync();
      var router = new Router();
      var publication = new PublicationProbe(db);
      var services = new PlanningTestServices(
        db,
        router,
        publicationScope: publication
      );
      var profile = await services.Routes.ProfileAsync(truck.Id, default);
      var pair = DeadheadConnection.Find(loads[1], loads)!;
      db.DispatchDeadheads.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = loads[1].Id,
          PreviousDispatchId = loads[0].Id,
          InputHash = pair.Signature(profile),
          Miles = 80,
          RouteJson = geometry,
          CalculatedAt = DateTime.UtcNow.AddDays(-1),
          RetryAfter = DateTime.UtcNow.AddMinutes(-1),
          ErrorMessage = "Old synthetic failure",
        }
      );
      await db.SaveChangesAsync();
      return new(
        connection,
        db,
        loads[1],
        services,
        profile,
        router,
        publication
      );
    }

    private static Load CreateLoad(Guid truck, int day) =>
      new()
      {
        Id = Guid.NewGuid(),
        LoadNumber = day,
        TruckId = truck,
        Status = "assigned",
        Price = 1000,
        LoadedMiles = 400,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Job = "Pick Up",
            ScheduledDate = new(2026, 9, day),
            Latitude = 40,
            Longitude = -80,
          },
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 2,
            Job = "Drop Off",
            ScheduledDate = new(2026, 9, day + 1),
            Latitude = 41,
            Longitude = -79,
          },
        ],
      };

    public async ValueTask DisposeAsync()
    {
      services.Dispose();
      await db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }
    public int GeocodeCalls { get; private set; }
    public Func<
      string,
      CancellationToken,
      Task<RoutePoint>
    > Geocode { get; set; } = (_, _) => throw new NotSupportedException();
    public Func<
      IReadOnlyList<RoutePoint>,
      CancellationToken,
      Task<TruckRoute>
    > Read { get; set; } = (points, _) => Task.FromResult(Complete(points));

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      GeocodeCalls++;
      return Geocode(address, ct);
    }

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Calls++;
      return Read(points, ct);
    }
  }
}
