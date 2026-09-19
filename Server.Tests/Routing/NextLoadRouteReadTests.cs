using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Features.Execution.Models;
using Application.Features.Routing.Background;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Options;
using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class NextLoadRouteReadTests
{
  [Theory]
  [InlineData("Drop Off", false)]
  [InlineData("Drop Off", true)]
  [InlineData("Delivery", true)]
  [InlineData("Drop", true)]
  public async Task NativeDeliveryConnectionIsVisibleAndRejectsChangedRevision(
    string ending,
    bool duplicate
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var root = fixture.Loads[0];
    root.Stops[^1].Job = ending;
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = fixture.Truck,
      Status = "active",
      Revision = 5,
      Stops = ExecutionStopRows.Capture(root.Stops),
      Loads = [new() { Id = Guid.NewGuid(), DispatchId = root.Id }],
    };
    fixture.Db.AddRange(trip, leg);
    var planned = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = fixture.Truck,
      Status = "planned",
      Stops = ExecutionStopRows.Capture(ExecutionStopRows.Read(leg)),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = root.Id,
          Sequence = 1,
        },
      ],
    };
    if (duplicate)
      fixture.Db.ExecutionLegs.Add(planned);
    await fixture.Db.SaveChangesAsync();
    if (ending == "Drop")
    {
      var transfer = (
        await fixture.Handler.Handle(
          new(fixture.Truck, root.Id, CurrentExecutionLegId: leg.Id),
          default
        )
      ).Response!;
      Assert.Contains(
        transfer.Routes!,
        route => route.ExecutionLegId == planned.Id
      );
      Assert.Null(
        transfer
          .Routes!.Single(route => route.Id == fixture.Loads[1].Id)
          .Deadhead
      );
      return;
    }
    var current = await fixture.Services.Routes.LoadAsync(
      root.Id,
      default,
      leg.Id,
      fixture.Truck
    );
    var future = await fixture.Services.Routes.LoadAsync(
      fixture.Loads[1].Id,
      default,
      null,
      fixture.Truck
    );
    var pair = DeadheadConnection.Find(future, [current])!;
    var saved = await fixture.Db.DispatchDeadheads.SingleAsync(x =>
      x.DispatchId == future.Id
    );
    var profile = await fixture.Services.Routes.ProfileAsync(
      fixture.Truck,
      default
    );
    saved.InputHash = pair.Signature(profile);
    saved.PreviousExecutionLegId = leg.Id;
    await fixture.Db.SaveChangesAsync();

    var first = (
      await fixture.Handler.Handle(
        new(fixture.Truck, root.Id, CurrentExecutionLegId: leg.Id),
        default
      )
    ).Response!;

    Assert.Equal(2, first.Routes!.Count);
    Assert.DoesNotContain(
      first.Routes,
      route => route.ExecutionLegId == planned.Id
    );
    Assert.Equal("ready", first.Routes![0].Status);
    Assert.NotNull(first.Routes[0].Deadhead);
    Assert.NotNull(first.Routes[1].Deadhead);
    Assert.Empty(await fixture.Db.SourceRoadRequests.ToListAsync());
    var unchanged = (
      await fixture.Handler.Handle(
        new(fixture.Truck, root.Id, first.Revision, leg.Id),
        default
      )
    ).Response!;
    Assert.True(unchanged.Unchanged);
    Assert.Equal(1, fixture.Reader.GeometryReads);

    leg.Revision++;
    await fixture.Db.SaveChangesAsync();
    var changed = (
      await fixture.Handler.Handle(
        new(fixture.Truck, root.Id, first.Revision, leg.Id),
        default
      )
    ).Response!;

    Assert.NotEqual(first.Revision, changed.Revision);
    Assert.Null(changed.Routes![0].Deadhead);
    Assert.NotNull(changed.Routes[1].Deadhead);
    Assert.Equal(
      future.Id,
      Assert
        .Single(await fixture.Db.SourceRoadRequests.ToListAsync())
        .DispatchId
    );
  }

  [Theory]
  [InlineData("Versions", false)]
  [InlineData("Saved", true)]
  public void PostgreSqlSnapshotQueriesTranslateToOneJoinedRead(
    string method,
    bool geometry
  )
  {
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=unused;Database=translation_only")
        .Options
    );
    var reader = new NextLoadRouteReader(db);
    var query = (IQueryable)
      typeof(NextLoadRouteReader)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(reader, [new[] { Guid.NewGuid(), Guid.NewGuid() }])!;
    var sql = query.ToQueryString();
    Assert.Contains("LEFT JOIN", sql);
    Assert.Contains("DispatchBaseRoutes", sql);
    Assert.Contains("DispatchDeadheads", sql);
    var projection = sql[..sql.IndexOf("\nFROM ", StringComparison.Ordinal)];
    Assert.Equal(
      geometry,
      Regex.IsMatch(
        projection,
        @"(?:SELECT|,)\s*\w+\.""RouteJson""(?:\s+AS\s+""[^""]+"")?\s*(?=,|FROM|$)",
        RegexOptions.Multiline
      )
    );
    if (!geometry)
      Assert.Matches(
        @"(?:.*""RouteJson"" IS NOT NULL AND \w+\.""RouteJson"" <> ''){2}",
        projection
      );
  }

  [Fact]
  public async Task ColdReadUsesOneGeometryBatchAndUnchangedOrLabelsOnlyReadsNeverFetchGeometry()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = (
      await fixture.Handler.Handle(
        new(fixture.Truck, fixture.Loads[0].Id),
        default
      )
    ).Response!;
    Assert.Equal(2, first.Routes!.Count);
    Assert.All(first.Routes, route => Assert.Equal("ready", route.Status));
    Assert.DoesNotContain(
      first.Routes,
      route => route.Id == fixture.Loads[0].Id
    );
    Assert.All(first.Routes, route => Assert.NotNull(route.Deadhead));
    Assert.Equal(1, fixture.Reader.GeometryReads);
    Assert.Equal(0, fixture.Reader.VersionReads);
    fixture.Probe.Columns.Clear();
    var unchanged = (
      await fixture.Handler.Handle(
        new(fixture.Truck, fixture.Loads[0].Id, first.Revision),
        default
      )
    ).Response!;
    Assert.True(unchanged.Unchanged);
    Assert.Null(unchanged.Routes);
    Assert.Equal(1, fixture.Reader.GeometryReads);
    Assert.Equal(1, fixture.Reader.VersionReads);
    Assert.DoesNotContain(
      fixture.Probe.Columns.SelectMany(x => x),
      name => name is "RouteJson" or "PlanJson"
    );
    fixture.Loads[1].Stops[0].Name = "Updated pickup company";
    await fixture.Db.SaveChangesAsync();
    var labels = (
      await fixture.Handler.Handle(
        new(fixture.Truck, fixture.Loads[0].Id, first.Revision),
        default
      )
    ).Response!;
    Assert.False(labels.Unchanged);
    Assert.Null(labels.Routes);
    Assert.Contains(
      labels.Labels!,
      label => label.Names.Contains("Updated pickup company")
    );
    Assert.NotEqual(first.Revision, labels.Revision);
    Assert.Equal(1, fixture.Reader.GeometryReads);
    Assert.DoesNotContain(
      fixture.Probe.Columns.SelectMany(x => x),
      name => name is "RouteJson" or "PlanJson"
    );
  }

  [Fact]
  public async Task InsertedDispatchInvalidatesSuccessorConnectionAndMissingRoutesKeepTheirStopNumbers()
  {
    await using var fixture = await Fixture.CreateAsync();
    var original = (
      await fixture.Handler.Handle(
        new(fixture.Truck, fixture.Loads[0].Id),
        default
      )
    ).Response!;
    var inserted = Fixture.Load(fixture.Truck, 4, 1);
    fixture.Db.Dispatches.Add(inserted);
    await fixture.Db.SaveChangesAsync();
    var changed = (
      await fixture.Handler.Handle(
        new(fixture.Truck, fixture.Loads[0].Id, original.Revision),
        default
      )
    ).Response!;
    Assert.NotEqual(original.Revision, changed.Revision);
    var pending = changed.Routes!.Single(x => x.Id == inserted.Id);
    Assert.Equal("pending", pending.Status);
    Assert.Equal(2, pending.StopCount);
    Assert.Null(
      changed.Routes!.Single(x => x.Id == fixture.Loads[1].Id).Deadhead
    );
    Assert.NotNull(
      changed.Routes!.Single(x => x.Id == fixture.Loads[2].Id).Deadhead
    );
    var profile = await fixture.Services.Routes.ProfileAsync(
      fixture.Truck,
      default
    );
    profile.HeightFeet += .5;
    await fixture.Services.Routes.SaveProfileAsync(
      fixture.Loads[0].Id,
      profile,
      default
    );
    var resized = (
      await fixture.Handler.Handle(
        new(fixture.Truck, fixture.Loads[0].Id, changed.Revision),
        default
      )
    ).Response!;
    Assert.All(
      resized.Routes!,
      route =>
      {
        Assert.Equal("pending", route.Status);
        Assert.Null(route.Deadhead);
      }
    );
  }

  [Fact]
  public async Task FollowingInTransitLoadKeepsItsSavedGeometryAndRevisionValidation()
  {
    await using var fixture = await Fixture.CreateAsync();
    var current = fixture.Loads[0];
    var next = fixture.Loads[1];
    next.Status = "in_transit";
    next.PlanningTruckId = fixture.Truck;
    next.PlanningFromStopId = next.Stops[0].Id;
    next.TruckId = null;
    await fixture.Db.SaveChangesAsync();

    var first = (
      await fixture.Handler.Handle(new(fixture.Truck, current.Id), default)
    ).Response!;
    Assert.Equal(
      new[] { next.Id, fixture.Loads[2].Id },
      first.Routes!.Select(x => x.Id)
    );
    var route = first.Routes![0];
    Assert.Equal("ready", route.Status);
    Assert.Equal(next.Stops.Select(x => x.Id), route.Stops.Select(x => x.Id));
    Assert.NotEmpty(route.Legs);
    Assert.DoesNotContain(first.Routes, x => x.Id == current.Id);
    var unchanged = (
      await fixture.Handler.Handle(
        new(fixture.Truck, current.Id, first.Revision),
        default
      )
    ).Response!;
    Assert.True(unchanged.Unchanged);
    Assert.Equal(1, fixture.Reader.GeometryReads);
  }

  [Theory]
  [InlineData("{")]
  [InlineData("{\"legs\":null}")]
  public async Task CorruptBaseGeometryReturnsPendingAndQueuesRepair(
    string json
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = fixture.Loads[1].Id;
    (
      await fixture.Db.DispatchBaseRoutes.SingleAsync(x => x.DispatchId == id)
    ).RouteJson = json;
    await fixture.Db.SaveChangesAsync();
    var response = (
      await fixture.Handler.Handle(
        new(fixture.Truck, fixture.Loads[0].Id),
        default
      )
    ).Response!;
    var route = response.Routes!.Single(x => x.Id == id);
    Assert.Equal("pending", route.Status);
    Assert.Equal(2, route.StopCount);
    Assert.Empty(route.Stops);
    Assert.Equal(
      id,
      Assert
        .Single(await fixture.Db.SourceRoadRequests.ToListAsync())
        .DispatchId
    );
  }

  [Theory]
  [InlineData(null)]
  [InlineData("{")]
  [InlineData("{\"legs\":null}")]
  public async Task MissingOrCorruptDeadheadGeometryKeepsTheBaseRouteAndQueuesRepair(
    string? json
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var id = fixture.Loads[1].Id;
    (
      await fixture.Db.DispatchDeadheads.SingleAsync(x => x.DispatchId == id)
    ).RouteJson = json;
    await fixture.Db.SaveChangesAsync();
    var response = (
      await fixture.Handler.Handle(
        new(fixture.Truck, fixture.Loads[0].Id),
        default
      )
    ).Response!;
    var route = response.Routes!.Single(x => x.Id == id);
    Assert.Equal("ready", route.Status);
    Assert.Null(route.Deadhead);
    Assert.Equal(
      id,
      Assert
        .Single(await fixture.Db.SourceRoadRequests.ToListAsync())
        .DispatchId
    );
  }

  private sealed class Fixture(
    SqliteConnection connection,
    AppDbContext db,
    QueryColumnProbe probe,
    Guid truck,
    List<Load> loads,
    PlanningTestServices services,
    CountingReader reader,
    GetNextLoadRoutesHandler handler,
    RoutePreparationQueue queue
  ) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public QueryColumnProbe Probe => probe;
    public Guid Truck => truck;
    public List<Load> Loads => loads;
    public PlanningTestServices Services => services;
    public CountingReader Reader => reader;
    public GetNextLoadRoutesHandler Handler => handler;
    public RoutePreparationQueue Queue => queue;

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var probe = new QueryColumnProbe();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .AddInterceptors(probe)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      var truck = new Truck { Id = Guid.NewGuid() };
      db.Trucks.Add(truck);
      var loads = new[]
      {
        Load(truck.Id, 1, 0),
        Load(truck.Id, 2, 2),
        Load(truck.Id, 3, 4),
      }.ToList();
      loads[0].Status = "in_transit";
      db.Dispatches.AddRange(loads);
      await db.SaveChangesAsync();
      var services = new PlanningTestServices(db);
      var profile = await services.Routes.ProfileAsync(truck.Id, default);
      var stored = await db
        .Dispatches.AsNoTracking()
        .Include(x => x.Stops)
        .OrderBy(x => x.LoadNumber)
        .ToListAsync();
      foreach (var load in stored.Skip(1))
      {
        var route = new TruckRoute
        {
          CalculatedAt = DateTime.UtcNow,
          Miles = 100,
          Seconds = 6000,
          Legs = [new(100, 6000, [new(40, -80), new(41, -80)])],
        };
        db.DispatchBaseRoutes.Add(
          new()
          {
            DispatchId = load.Id,
            InputHash = BaseRouteService.Signature(load, profile),
            CalculatedAt = route.CalculatedAt,
            RouteJson = JsonSerializer.Serialize(
              route,
              RoutePlanningService.Json
            ),
          }
        );
        var pair = DeadheadConnection.Find(load, stored)!;
        var deadhead = new TruckRoute
        {
          CalculatedAt = route.CalculatedAt,
          Miles = 10,
          Seconds = 600,
          Legs =
          [
            new(
              10,
              600,
              [
                new((double)pair.From.Latitude!, (double)pair.From.Longitude!),
                new((double)pair.To.Latitude!, (double)pair.To.Longitude!),
              ]
            ),
          ],
        };
        db.DispatchDeadheads.Add(
          new()
          {
            DispatchId = load.Id,
            PreviousDispatchId = pair.Previous.Id,
            InputHash = pair.Signature(profile),
            Miles = 10,
            CalculatedAt = route.CalculatedAt,
            RouteJson = JsonSerializer.Serialize(
              deadhead,
              RoutePlanningService.Json
            ),
          }
        );
      }
      await db.SaveChangesAsync();
      var reader = new CountingReader(new NextLoadRouteReader(db));
      var queue = new RoutePreparationQueue(
        Options.Create(new RoutePreparationOptions()),
        TimeProvider.System
      );
      return new(
        connection,
        db,
        probe,
        truck.Id,
        loads,
        services,
        reader,
        new(
          reader,
          services.DeadheadHistory,
          services.Routes,
          new SourceRoadDemand(new SourceRoadStore(db), TimeProvider.System),
          services.Sender
        ),
        queue
      );
    }

    public static Load Load(Guid truck, int number, int day) =>
      new()
      {
        Id = Guid.NewGuid(),
        TruckId = truck,
        LoadNumber = number,
        Status = "assigned",
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Job = "Pick Up",
            Name = "Pickup",
            Latitude = 40,
            Longitude = -80,
            ScheduledDate = new DateOnly(2026, 9, 8).AddDays(day),
            ScheduledTime = new TimeOnly(8, 0),
          },
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 2,
            Job = "Drop Off",
            Name = "Delivery",
            Latitude = 41,
            Longitude = -80,
            ScheduledDate = new DateOnly(2026, 9, 8).AddDays(day),
            ScheduledTime = new TimeOnly(16, 0),
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

  private sealed class CountingReader(INextLoadRouteReader inner)
    : INextLoadRouteReader
  {
    public int VersionReads { get; private set; }
    public int GeometryReads { get; private set; }

    public Task<IReadOnlyList<Load>> ReadLoadsAsync(
      Guid truckId,
      CancellationToken ct
    ) => inner.ReadLoadsAsync(truckId, ct);

    public Task<IReadOnlyList<NextLoadRouteVersion>> ReadVersionsAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct
    )
    {
      VersionReads++;
      return inner.ReadVersionsAsync(ids, ct);
    }

    public Task<
      IReadOnlyDictionary<Guid, SavedNextLoadRoute>
    > ReadGeometryAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
      GeometryReads++;
      return inner.ReadGeometryAsync(ids, ct);
    }

    public Task<IReadOnlyList<NextLoadRouteVersion>> ReadExecutionVersionsAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct
    )
    {
      VersionReads++;
      return inner.ReadExecutionVersionsAsync(ids, ct);
    }

    public Task<
      IReadOnlyDictionary<Guid, SavedNextLoadRoute>
    > ReadExecutionGeometryAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct
    )
    {
      GeometryReads++;
      return inner.ReadExecutionGeometryAsync(ids, ct);
    }
  }
}
