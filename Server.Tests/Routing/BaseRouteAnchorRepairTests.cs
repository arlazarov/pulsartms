using System.Text.Json;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class BaseRouteAnchorRepairTests
{
  [Theory]
  [InlineData("start")]
  [InlineData("end")]
  [InlineData("join")]
  public async Task MatchingHashRepairsOnlyTheBadLegAndReusesTheRepairedResult(
    string changed
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var original = Route(fixture.Points);
    var leg = original.Legs[1];
    var offset = changed == "join" ? .08 : 2.61;
    var shifted = leg.Points[changed == "end" ? 1 : 0] with
    {
      Latitude = leg.Points[changed == "end" ? 1 : 0].Latitude + offset / 69,
    };
    original.Legs[1] =
      changed == "end"
        ? leg with
        {
          Points = [leg.Points[0], shifted],
        }
        : leg with
        {
          Points = [shifted, leg.Points[1]],
        };
    await fixture.SaveAsync(original);
    var callerTracked = Assert.Single(fixture.Db.DispatchBaseRoutes.Local);
    var rebuilt = await fixture.Service.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default,
      fixture.Points
    );
    Assert.Same(
      callerTracked,
      Assert.Single(fixture.Db.DispatchBaseRoutes.Local)
    );
    Assert.NotEmpty(callerTracked.RouteJson);
    Assert.True(RouteAnchoring.Matches(rebuilt, fixture.Points));
    var requested = Assert.Single(fixture.Router.Requests);
    Assert.Equal(fixture.Points.Skip(1).Take(2), requested);
    Assert.Equal(original.Legs[0].Miles, rebuilt.Legs[0].Miles);
    Assert.Equal(original.Legs[0].Seconds, rebuilt.Legs[0].Seconds);
    Assert.Equal(original.Legs[0].Points, rebuilt.Legs[0].Points);
    Assert.Equal(original.Legs[2].Points, rebuilt.Legs[2].Points);
    Assert.Equal(original.Legs[2].Miles, rebuilt.Legs[2].Miles);
    var saved = await fixture
      .Db.DispatchBaseRoutes.AsNoTracking()
      .SingleAsync();
    using var json = JsonDocument.Parse(saved.RouteJson);
    Assert.False(json.RootElement.TryGetProperty("points", out _));
    await fixture.Service.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default,
      fixture.Points
    );
    Assert.Single(fixture.Router.Requests);
    Assert.Equal(0, fixture.Router.Geocodes);
  }

  [Fact]
  public async Task ValidSavedSnappingDoesNotTriggerAnyProviderCalls()
  {
    await using var fixture = await Fixture.CreateAsync();
    var points = fixture
      .Points.Select(point =>
        point with
        {
          Latitude = point.Latitude + .08044 / 69,
        }
      )
      .ToArray();
    var original = Route(points);
    await fixture.SaveAsync(original);
    var saved = await fixture.Service.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default,
      fixture.Points
    );
    Assert.True(RouteAnchoring.Matches(saved, fixture.Points));
    Assert.Empty(fixture.Router.Requests);
    Assert.Equal(0, fixture.Router.Geocodes);
  }

  [Fact]
  public async Task FreshWrongEndpointFailsBeforeReplacingTheExistingBaseRoute()
  {
    await using var fixture = await Fixture.CreateAsync();
    var original = Route(fixture.Points);
    original.Legs[^1] = original.Legs[^1] with
    {
      Points = [fixture.Points[^2], new(45, -78)],
    };
    await fixture.SaveAsync(original);
    var before = (
      await fixture.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync()
    ).RouteJson;
    fixture.Router.WrongEnd = true;
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Service.EnsureAsync(
          fixture.Load,
          fixture.Profile,
          default,
          fixture.Points
        )
    );
    Assert.Equal(
      before,
      (
        await fixture.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync()
      ).RouteJson
    );
    Assert.Single(fixture.Router.Requests);
  }

  [Theory]
  [InlineData("new")]
  [InlineData("repair")]
  [InlineData("reuse")]
  public async Task SequentialLoadsDoNotRetainOwnedBaseRouteEntities(
    string mode
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var loads = new[] { fixture.Load }
      .Concat(
        Enumerable
          .Range(1, 2)
          .Select(number => new Load
          {
            Id = Guid.NewGuid(),
            LoadNumber = number,
            Stops = fixture
              .Load.Stops.Select(stop => new DispatchStop
              {
                Id = Guid.NewGuid(),
                Sequence = stop.Sequence,
                Latitude = stop.Latitude,
                Longitude = stop.Longitude,
              })
              .ToList(),
          })
      )
      .ToArray();
    fixture.Db.Dispatches.AddRange(loads.Skip(1));
    await fixture.Db.SaveChangesAsync();
    if (mode != "new")
      foreach (var load in loads)
      {
        var route = Route(fixture.Points);
        if (mode == "repair")
          route.Legs[^1] = route.Legs[^1] with
          {
            Points = [fixture.Points[^2], new(45, -78)],
          };
        var entity = new DispatchBaseRoute
        {
          Id = Guid.NewGuid(),
          DispatchId = load.Id,
          InputHash = BaseRouteService.Signature(load, fixture.Profile),
          RouteJson = JsonSerializer.Serialize(route, RoutingJson.Options),
        };
        fixture.Db.DispatchBaseRoutes.Add(entity);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.Entry(entity).State = EntityState.Detached;
      }
    foreach (var load in loads)
    {
      var first = await fixture.Service.EnsureAsync(
        load,
        fixture.Profile,
        default,
        fixture.Points
      );
      Assert.Empty(fixture.Db.DispatchBaseRoutes.Local);
      var saved = await fixture
        .Db.DispatchBaseRoutes.AsNoTracking()
        .SingleAsync(row => row.DispatchId == load.Id);
      var second = await fixture.Service.EnsureAsync(
        load,
        fixture.Profile,
        default,
        fixture.Points
      );
      Assert.Equal(
        RoutePlanStorage.Serialize(first),
        RoutePlanStorage.Serialize(second)
      );
      Assert.Equal(
        first.Miles,
        SavedRouteReader.Route(saved.RouteJson, 3)!.Miles
      );
      Assert.Empty(fixture.Db.DispatchBaseRoutes.Local);
      Assert.Same(
        load,
        fixture.Db.Dispatches.Local.Single(row => row.Id == load.Id)
      );
    }
    Assert.Equal(mode == "reuse" ? 0 : 3, fixture.Router.Requests.Count);
  }

  [Fact]
  public async Task ReusePreservesCallerTrackedBaseRouteAndUnsavedRelatedChanges()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.SaveAsync(Route(fixture.Points));
    var existing = Assert.Single(fixture.Db.DispatchBaseRoutes.Local);
    existing.CalculatedAt = DateTime.UtcNow.AddDays(-1);
    fixture.Load.CustomerName = "Pending customer edit";
    fixture.Db.ChangeTracker.DetectChanges();
    var originalJson = existing.RouteJson;
    await fixture.Service.EnsureAsync(
      fixture.Load,
      fixture.Profile,
      default,
      fixture.Points
    );
    Assert.Same(existing, Assert.Single(fixture.Db.DispatchBaseRoutes.Local));
    Assert.Equal(EntityState.Modified, fixture.Db.Entry(existing).State);
    Assert.Equal(EntityState.Modified, fixture.Db.Entry(fixture.Load).State);
    Assert.Equal("Pending customer edit", fixture.Load.CustomerName);
    Assert.NotEqual(
      fixture.Load.CustomerName,
      (await fixture.Db.Dispatches.AsNoTracking().SingleAsync()).CustomerName
    );
    Assert.Equal(originalJson, existing.RouteJson);
    Assert.Empty(fixture.Router.Requests);
  }

  [Fact]
  public async Task FailedRepairDoesNotRetainOwnedEntityOrDiscardPendingChanges()
  {
    await using var fixture = await Fixture.CreateAsync();
    var route = Route(fixture.Points);
    route.Legs[^1] = route.Legs[^1] with
    {
      Points = [fixture.Points[^2], new(45, -78)],
    };
    await fixture.SaveAsync(route);
    fixture.Db.Entry(Assert.Single(fixture.Db.DispatchBaseRoutes.Local)).State =
      EntityState.Detached;
    fixture.Load.CustomerName = "Pending customer edit";
    fixture.Router.WrongEnd = true;
    await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Service.EnsureAsync(
          fixture.Load,
          fixture.Profile,
          default,
          fixture.Points
        )
    );
    Assert.Empty(fixture.Db.DispatchBaseRoutes.Local);
    Assert.Equal(EntityState.Modified, fixture.Db.Entry(fixture.Load).State);
    Assert.Equal("Pending customer edit", fixture.Load.CustomerName);
  }

  private static TruckRoute Route(IReadOnlyList<RoutePoint> points) =>
    new()
    {
      Legs = points
        .Zip(points.Skip(1), (from, to) => new RouteLeg(100, 6000, [from, to]))
        .ToList(),
      Points = points.ToList(),
      Miles = (points.Count - 1) * 100,
      Seconds = (points.Count - 1) * 6000,
    };

  private sealed class Fixture(
    SqliteConnection connection,
    AppDbContext db,
    Load load
  ) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public Load Load => load;
    public TruckRouteProfile Profile { get; } =
      new() { UsesFleetDefaults = true };
    public Router Router { get; } = new();
    public RoutePoint[] Points =>
      load
        .Stops.OrderBy(stop => stop.Sequence)
        .Select(stop => new RoutePoint(
          (double)stop.Latitude!,
          (double)stop.Longitude!
        ))
        .ToArray();
    private PlanningTestServices? services;
    public BaseRouteService Service =>
      (services ??= new(db, Router)).BaseRoutes;

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
      var load = new Load
      {
        Id = Guid.NewGuid(),
        Stops = Enumerable
          .Range(0, 4)
          .Select(index => new DispatchStop
          {
            Id = Guid.NewGuid(),
            Sequence = index + 1,
            Latitude = 40 + index,
            Longitude = -80 + index,
          })
          .ToList(),
      };
      db.Dispatches.Add(load);
      await db.SaveChangesAsync();
      return new(connection, db, load);
    }

    public async Task SaveAsync(TruckRoute route)
    {
      db.DispatchBaseRoutes.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = load.Id,
          InputHash = BaseRouteService.Signature(load, Profile),
          RouteJson = JsonSerializer.Serialize(route, RoutingJson.Options),
        }
      );
      await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
      services?.Dispose();
      await db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class Router : IRoutingProvider
  {
    public bool IsConfigured => true;
    public List<RoutePoint[]> Requests { get; } = [];
    public int Geocodes { get; private set; }
    public bool WrongEnd { get; set; }

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      Geocodes++;
      throw new NotSupportedException();
    }

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Requests.Add(points.ToArray());
      var route = Route(points);
      if (WrongEnd)
        route.Legs[^1] = route.Legs[^1] with
        {
          Points = [points[^2], new(45, -78)],
        };
      return Task.FromResult(route);
    }
  }
}
