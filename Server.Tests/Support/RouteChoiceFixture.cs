using Application.Features.Dispatch.Queries;
using Application.Features.Execution.Models;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Support;

internal sealed class RouteChoiceFixture : IAsyncDisposable
{
  private readonly SqliteConnection connection = new("Data Source=:memory:");
  public AppDbContext Db { get; private set; } = null!;
  public PlanningTestServices Planning { get; private set; } = null!;
  public ManualTimeProvider Clock { get; } = new();
  public RouteChoiceDrafts Drafts { get; private set; } = null!;
  public RouteChoiceService Choices { get; private set; } = null!;
  public Router Routing { get; } = new();
  public PublicationProbe Publication { get; private set; } = null!;
  public Guid Owner { get; } = Guid.NewGuid();
  public TruckLocation? Location { get; set; }
  public Truck Truck { get; } =
    new() { Id = Guid.NewGuid(), ExternalId = "route-choice-fixture" };
  public DispatchEntity Load { get; } =
    new()
    {
      Id = Guid.NewGuid(),
      LoadNumber = 1383,
      Status = "assigned",
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Latitude = 40,
          Longitude = -80,
          Job = "Pick Up",
        },
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 2,
          Latitude = 43,
          Longitude = -80,
          Job = "Drop Off",
        },
      ],
    };

  public static async Task<RouteChoiceFixture> CreateAsync(
    PublicationCommitFailureProbe? commits = null
  )
  {
    var fixture = new RouteChoiceFixture();
    await fixture.connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(
      fixture.connection
    );
    if (commits is not null)
      options.AddInterceptors(commits);
    fixture.Db = new(options.Options);
    await fixture.Db.Database.EnsureCreatedAsync();
    fixture.Load.TruckId = fixture.Truck.Id;
    fixture.Db.Trucks.Add(fixture.Truck);
    fixture.Db.Dispatches.Add(fixture.Load);
    fixture.Db.Users.Add(
      new()
      {
        Id = fixture.Owner,
        IdentityUserId = "fixture-route-choice",
        Name = "Fixture",
        Email = "route@example.invalid",
      }
    );
    await fixture.Db.SaveChangesAsync();
    fixture.Db.ChangeTracker.Clear();
    var sender = new Sender(fixture);
    fixture.Publication = new(fixture.Db);
    fixture.Planning = new(
      fixture.Db,
      fixture.Routing,
      sender,
      publicationScope: fixture.Publication
    );
    fixture.Drafts = new(fixture.Db, fixture.Clock);
    fixture.Choices = new(
      fixture.Db,
      fixture.Planning.Routes,
      fixture.Routing,
      fixture.Routing,
      fixture.Drafts,
      fixture.Clock,
      fixture.Planning.Reads,
      TestCache.Preparation(),
      NullLogger<RouteChoiceService>.Instance,
      fixture.Planning.PlanningInputs,
      new(
        fixture.Db,
        fixture.Planning.Reads,
        new(
          fixture.Db,
          fixture.Planning.Reads,
          fixture.Planning.Settings,
          fixture.Planning.ExchangeRates
        )
      ),
      fixture.Planning.Publication,
      fixture.Planning.Profiles
    );
    return fixture;
  }

  public Task<RouteChoicePreview> Preview() =>
    Choices.PreviewAsync(Load.Id, Owner, new([], true), default);

  public async Task<ExecutionLeg> AddExecutionAsync(string status)
  {
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = Truck.Id,
      Trip = new() { Id = Guid.NewGuid() },
      Status = status,
      Revision = 3,
      Stops = ExecutionStopRows.Capture(Load.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = Load.Id,
          StartVisitId = Load.Stops[0].Id,
          EndVisitId = Load.Stops[^1].Id,
        },
      ],
    };
    Db.ExecutionLegs.Add(leg);
    await Db.SaveChangesAsync();
    Db.ChangeTracker.Clear();
    return leg;
  }

  public async Task StartTripAsync()
  {
    var profile = await Planning.Routes.ProfileAsync(Truck.Id, default);
    await Planning.Routes.BuildAsync(Load.Id, new(profile), default);
    var load = await Db
      .Dispatches.Include(x => x.Stops)
      .SingleAsync(x => x.Id == Load.Id);
    load.Status = "in_transit";
    load.Stops.OrderBy(s => s.Sequence).First().ManualCompletedAt = Clock
      .GetUtcNow()
      .UtcDateTime.AddHours(-1);
    load.Stops.OrderBy(s => s.Sequence).First().ManualCompletionRevision = 1;
    var saved = await Db.DispatchRoutePlans.SingleAsync(x =>
      x.DispatchId == Load.Id
    );
    saved.InputHash = RoutePlanInputs.Hash(load, profile);
    await Db.SaveChangesAsync();
    Db.ChangeTracker.Clear();
    Planning.Reads.Invalidate("dispatch");
    Planning.Reads.Invalidate("board");
    Planning.Reads.Invalidate($"route:{Load.Id}");
    Location = new()
    {
      TruckId = Truck.Id,
      Latitude = 41.5m,
      Longitude = -80m,
      Speed = 60,
      UpdatedAt = Clock.GetUtcNow().UtcDateTime,
      ObservedAt = Clock.GetUtcNow().UtcDateTime,
    };
  }

  public async ValueTask DisposeAsync()
  {
    Planning.Dispose();
    await Db.DisposeAsync();
    await connection.DisposeAsync();
  }

  private sealed class Sender(RouteChoiceFixture fixture) : ISender
  {
    public async Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    ) =>
      request switch
      {
        GetDispatchBoardQuery query => (TResponse)
          (object)await fixture.Planning.Board.Handle(query, ct),
        GetFleetLocationsQuery => (TResponse)
          (object)
            RequestResponse<FleetLocationsResponse>.Ok(
              new() { Trucks = fixture.Location is { } truck ? [truck] : [] }
            ),
        _ => throw new NotSupportedException(),
      };

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(object request, CancellationToken ct = default) =>
      throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }

  internal sealed class Router : IRoutingProvider, IRouteAlternativesProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }
    public bool MissVia { get; set; }
    public Func<Task>? BeforeCalculate { get; set; }
    public int OptionCount { get; set; } = 2;
    public bool DuplicateOptions { get; set; }
    public IReadOnlyList<RoutePoint> LastPoints { get; private set; } = [];

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public async Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Calls++;
      if (BeforeCalculate is { } before)
        await before();
      LastPoints = points.ToList();
      return RouteViaGeometry.Join(
        points
          .Zip(points.Skip(1), (a, b) => new RouteLeg(100, 3600, [a, b]))
          .ToList(),
        [],
        DateTime.UtcNow
      );
    }

    public async Task<IReadOnlyList<TruckRoute>> CalculateAlternativesAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      var direct = await CalculateAsync(points, profile, ct);
      var alternate = RouteViaGeometry.Join(
        direct
          .Legs.Select(l => new RouteLeg(
            l.Miles + 10,
            l.Seconds + 600,
            [
              l.Points[0],
              new((l.Points[0].Latitude + l.Points[^1].Latitude) / 2, -79),
              l.Points[^1],
            ]
          ))
          .ToList(),
        [],
        DateTime.UtcNow
      );
      if (MissVia)
        direct.Legs[0].Points[^1] = new(50, -80);
      return OptionCount == 1 ? [direct]
        : DuplicateOptions ? [direct, direct]
        : [direct, alternate];
    }
  }
}
