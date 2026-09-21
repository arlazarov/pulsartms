using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fuel.Queries.GetFuelStations;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Domain.Models.Fleet;
using Domain.Models.Fuel;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Support;

using Application.Features.Execution.Models;
using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

internal sealed class SavedFuelHorizonFixture : IAsyncDisposable
{
  private SqliteConnection Connection { get; init; } = null!;
  public required AppDbContext Db { get; init; }
  public required PlanningTestServices Services { get; init; }
  public required PublicationProbe Publication { get; init; }
  public required Dispatch Current { get; init; }
  public required Dispatch Future { get; init; }
  public required RoutePlanningState State { get; init; }
  public required ForbiddenRouter Router { get; init; }
  public List<FuelStationDto> Stations { get; } = [];
  public Func<Task>? BeforePrices { get; set; }
  public FuelHorizon Horizon =>
    new(Services.FuelInputs, Db, Services.Deadheads);

  public static async Task<SavedFuelHorizonFixture> CreateAsync(
    FuelCommitFailureProbe? publication = null,
    HistoricalReadProbe? historyReads = null,
    QueryColumnProbe? queryColumns = null
  )
  {
    var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(
      connection
    );
    if (publication is not null)
      options.AddInterceptors(publication);
    if (historyReads is not null)
      options.AddInterceptors(historyReads);
    if (queryColumns is not null)
      options.AddInterceptors(queryColumns);
    var db = new AppDbContext(options.Options);
    await db.Database.EnsureCreatedAsync();
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "saved-fuel-horizon",
      UnitNumber = "Fuel",
      IsActive = true,
    };
    Dispatch Load(int number, DateOnly date, decimal from, decimal to) =>
      new()
      {
        Id = Guid.NewGuid(),
        Truck = truck,
        LoadNumber = number,
        Status = number == 1 ? "in_transit" : "assigned",
        ShipDate = date,
        DeliveryDate = date,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck.Id,
            Sequence = 1,
            Job = "Pick Up",
            ScheduledDate = date,
            Latitude = 40,
            Longitude = from,
          },
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = truck.Id,
            Sequence = 2,
            Job = "Drop Off",
            ScheduledDate = date,
            Latitude = 40,
            Longitude = to,
          },
        ],
      };
    var current = Load(1, today, -81, -79);
    current.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-1);
    var future = Load(2, today.AddDays(1), -78, -77);
    db.Dispatches.AddRange(current, future);
    await db.SaveChangesAsync();
    var profile = new TruckRouteProfile
    {
      Confirmed = true,
      TankGallons = 100,
      Mpg = 5,
      ReserveGallons = 10,
      FillPercent = 100,
      StopCostUsd = 20,
    };
    var baseRoute = Route(-78, -77);
    var persistedFuture = await db
      .Dispatches.AsNoTracking()
      .Include(load => load.Stops)
      .SingleAsync(load => load.Id == future.Id);
    db.DispatchBaseRoutes.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = future.Id,
        InputHash = BaseRouteService.Signature(persistedFuture, profile),
        RouteJson = RoutePlanStorage.Serialize(baseRoute),
        CalculatedAt = baseRoute.CalculatedAt,
      }
    );
    var history = await new DeadheadHistoryReader(db).ReadAsync(
      [future.Id],
      default
    );
    var pair =
      DeadheadConnection.Find(
        DeadheadHistoryProjection.Capture(history[future.Id])
      )
      ?? throw new InvalidOperationException(
        "The fixture requires a saved-route predecessor."
      );
    db.DispatchDeadheads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = future.Id,
        PreviousDispatchId = current.Id,
        InputHash = pair.Signature(profile),
        Miles = 100,
        RouteJson = RoutePlanStorage.Serialize(Route(-79, -78)),
      }
    );
    await db.SaveChangesAsync();
    var plan = new RoutePlan
    {
      TruckId = truck.Id,
      DispatchId = current.Id,
      FromCurrentPosition = true,
      Profile = profile,
      Route = Route(-80, -79),
      Stops =
      [
        new(current.Stops[1].Id, "Current delivery", "", 2, new(40, -79)),
      ],
    };
    plan.Tracking.NextStopId = current.Stops[1].Id;
    var state = new RoutePlanningState(
      profile,
      plan,
      new(0, 100, 6000, 0, false, false, DateTime.UtcNow, new(40, -80)),
      80,
      DateTime.UtcNow,
      false
    );
    var router = new ForbiddenRouter();
    var sender = new Sender(truck.Id);
    var publicationScope = new PublicationProbe(db);
    var services = new PlanningTestServices(
      db,
      router,
      sender,
      publicationScope: publicationScope
    );
    sender.Board = services.Board;
    var result = new SavedFuelHorizonFixture
    {
      Connection = connection,
      Db = db,
      Current = current,
      Future = future,
      State = state,
      Router = router,
      Services = services,
      Publication = publicationScope,
    };
    sender.Stations = result.Stations;
    sender.BeforePrices = () =>
      result.BeforePrices?.Invoke() ?? Task.CompletedTask;
    return result;
  }

  public static TruckRoute Route(double from, double to) =>
    new()
    {
      Miles = 100,
      Seconds = 6000,
      Legs = [new(100, 6000, [new(40, from), new(40, to)])],
    };

  public async Task<ExecutionLeg> ReceiveCurrentAsync()
  {
    var actor = Guid.NewGuid();
    var former = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "saved-fuel-former",
      UnitNumber = "Former",
    };
    var trip = new Trip { Id = Guid.NewGuid() };
    var operation = new DispatchSwitchOperation
    {
      Id = Guid.NewGuid(),
      IdempotencyKey = Guid.NewGuid(),
      Status = "completed",
      RecordedBy = actor,
    };
    ExecutionTransferVisit Visit(string job) =>
      new()
      {
        Id = Guid.NewGuid(),
        TripId = trip.Id,
        Operation = job,
        Latitude = 40,
        Longitude = -81,
        PlannedAt = Current.ShipDate!.Value.ToDateTime(TimeOnly.MinValue),
        ActualAt = DateTime.UtcNow.AddHours(-1),
        ConfirmedBy = actor,
      };
    var drop = Visit("Drop");
    var hook = Visit("Hook");
    var delivery = Current.Stops[^1];
    delivery.Address = "100 Verified Road";
    delivery.AddressVerifiedAt = DateTime.UtcNow;
    delivery.SourceAddressJson = StopAddress.From(delivery).Serialize();
    var outgoing = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = former.Id,
      Status = "completed",
      Revision = 3,
      EndSwitchId = operation.Id,
      Stops = ExecutionStopRows.Capture(
        [
          Current.Stops[0],
          ExecutionSnapshots.Boundary(drop, Current.Id, 2, "Loaded"),
        ]
      ),
    };
    var incoming = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = State.Plan!.TruckId,
      Status = "active",
      Revision = 7,
      StartSwitchId = operation.Id,
      Stops = ExecutionStopRows.Capture(
        [ExecutionSnapshots.Boundary(hook, Current.Id, 1, "Loaded"), delivery]
      ),
    };
    var participant = new SwitchParticipant
    {
      Id = Guid.NewGuid(),
      SwitchId = operation.Id,
      DispatchId = Current.Id,
      OutgoingLegId = outgoing.Id,
      IncomingLegId = incoming.Id,
      ReleaseVisitId = drop.Id,
      ReceiveVisitId = hook.Id,
      ReleasedBy = actor,
      ReceivedBy = actor,
      ReleasedAt = drop.ActualAt,
      ReceivedAt = hook.ActualAt,
    };
    outgoing.Loads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = Current.Id,
        Sequence = 1,
      }
    );
    incoming.Loads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = Current.Id,
        Sequence = 2,
      }
    );
    Db.AddRange(former, trip, operation, outgoing, incoming, participant);
    Current.Stops[0].TruckId = former.Id;
    State.Plan.ExecutionLegId = incoming.Id;
    State.Plan.AssignmentRevision = incoming.Revision;
    await Db.SaveChangesAsync();
    var native = await Services.Routes.LoadAsync(
      Current.Id,
      default,
      incoming.Id,
      State.Plan.TruckId
    );
    var next = await Services.Routes.LoadAsync(
      Future.Id,
      default,
      null,
      State.Plan.TruckId
    );
    var pair = DeadheadConnection.Find(next, [native])!;
    var saved = await Db.DispatchDeadheads.SingleAsync();
    saved.PreviousExecutionLegId = incoming.Id;
    saved.InputHash = pair.Signature(State.Profile);
    await Db.SaveChangesAsync();
    return incoming;
  }

  public async Task<DispatchBaseRoute> UseFutureFallbackAsync(
    TruckRouteProfile profile
  )
  {
    var basis = await Db.DispatchBaseRoutes.AsNoTracking().SingleAsync();
    var future = await Services.Routes.LoadAsync(Future.Id, default);
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      Version = 1,
      DispatchId = future.Id,
      TruckId = State.Plan!.TruckId,
      Profile = profile,
      FromCurrentPosition = false,
      Route = SavedRouteReader.Route(basis.RouteJson, 1)!,
    };
    Db.DispatchRoutePlans.Add(
      new()
      {
        Id = plan.Id,
        DispatchId = plan.DispatchId,
        TruckId = plan.TruckId,
        InputHash = RoutePlanInputs.Hash(future, profile),
        PlanJson = RoutePlanStorage.Serialize(plan),
      }
    );
    await Db.SaveChangesAsync();
    await Db.DispatchBaseRoutes.ExecuteDeleteAsync();
    Db.ChangeTracker.Clear();
    return basis;
  }

  public async Task<TruckRouteProfile> PrepareCalculationAsync()
  {
    var plan = State.Plan!;
    var profile = await Services.Routes.ProfileAsync(plan.TruckId, default);
    profile.Confirmed = true;
    await Services.Routes.SaveProfileAsync(
      Current.Id,
      profile,
      default,
      plan.ExecutionLegId,
      plan.AssignmentRevision
    );
    var current = await Services.Routes.LoadAsync(
      Current.Id,
      default,
      plan.ExecutionLegId,
      plan.TruckId
    );
    var future = await Services.Routes.LoadAsync(
      Future.Id,
      default,
      null,
      plan.TruckId
    );
    plan.Id = Guid.NewGuid();
    plan.Version = 1;
    plan.CalculatedAt = DateTime.UtcNow;
    plan.Profile = profile;
    Db.DispatchRoutePlans.Add(
      new()
      {
        Id = plan.Id,
        DispatchId = Current.Id,
        ExecutionLegId = plan.ExecutionLegId,
        AssignmentRevision = plan.AssignmentRevision,
        TruckId = plan.TruckId,
        CreatedAt = plan.CalculatedAt,
        InputHash = RoutePlanInputs.Hash(current, profile),
        PlanJson = RoutePlanStorage.Serialize(plan),
      }
    );
    (await Db.DispatchBaseRoutes.SingleAsync()).InputHash =
      BaseRouteService.Signature(future, profile);
    (await Db.DispatchDeadheads.SingleAsync()).InputHash = DeadheadConnection
      .Find(future, [current])!
      .Signature(profile);
    await Db.SaveChangesAsync();
    return profile;
  }

  private sealed class Sender(Guid truckId) : ISender
  {
    public GetDispatchBoardHandler Board { private get; set; } = null!;
    public List<FuelStationDto> Stations { private get; set; } = [];
    public Func<Task>? BeforePrices { private get; set; }

    public async Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      if (request is GetFuelStationsQuery && BeforePrices is { } before)
        await before();
      object result = request switch
      {
        GetDispatchBoardQuery board => await Board.Handle(board, ct),
        GetFuelStationsQuery => RequestResponse<List<FuelStationDto>>.Ok(
          Stations
        ),
        GetFleetLocationsQuery => RequestResponse<FleetLocationsResponse>.Ok(
          new()
          {
            Trucks =
            [
              new TruckLocation
              {
                TruckId = truckId,
                Latitude = 40,
                Longitude = -80,
                UpdatedAt = DateTime.UtcNow,
                FuelPercent = 80,
                FuelUpdatedAt = DateTime.UtcNow,
              },
            ],
          }
        ),
        _ => throw new NotSupportedException(),
      };
      return (TResponse)result;
    }

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

  public async ValueTask DisposeAsync()
  {
    Services.Dispose();
    await Db.DisposeAsync();
    await Connection.DisposeAsync();
  }

  internal sealed class ForbiddenRouter : IRoutingProvider
  {
    public bool IsConfigured => false;
    public int Calls { get; private set; }

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Calls++;
      throw new InvalidOperationException("Fuel must use saved roads only.");
    }

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      Calls++;
      throw new InvalidOperationException(
        "Fuel must use confirmed saved coordinates only."
      );
    }
  }
}
