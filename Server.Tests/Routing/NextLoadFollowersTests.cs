using System.Data.Common;
using Application.Caching;
using Application.Features.Routing.Background;
using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

// Stage 2c of docs/architecture/current-work.md: the map's next loads are
// the planning inputs' current work and the work after it, in the board's
// order (WorkOrderKey), not an order of their own or a current work the
// client supplies. The overlap cases run two database contexts at once over
// one shared in-memory database and one read cache.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class NextLoadFollowersTests
{
  // Same start: the board breaks the tie by load number, and so does the
  // map. The older rule broke it by id.
  [Fact]
  public async Task TiesAreBrokenByLoadNumber()
  {
    await using var f = await Fixture.CreateAsync();
    var current = f.Add(1, day: 0, status: "in_transit");
    var seven = f.Add(
      7,
      day: 2,
      id: Guid.Parse("00000000-0000-0000-0000-00000000000a")
    );
    var three = f.Add(
      3,
      day: 2,
      id: Guid.Parse("00000000-0000-0000-0000-00000000000b")
    );
    await f.SaveAsync();

    var routes = await f.NextAsync(current);

    Assert.Equal([three.Id, seven.Id], routes.Select(x => x.Id));
  }

  // A load without appointment dates is placed by its ship date, as on
  // the board; the older rule put it last.
  [Fact]
  public async Task MissingAppointmentDatesFallBackToTheShipDate()
  {
    await using var f = await Fixture.CreateAsync();
    var current = f.Add(1, day: 0, status: "in_transit");
    var later = f.Add(2, day: 3);
    var undated = f.Add(3, day: 1, dated: false);
    await f.SaveAsync();

    var routes = await f.NextAsync(current);

    Assert.Equal([undated.Id, later.Id], routes.Select(x => x.Id));
  }

  // The first load's route is passed and it now needs review. The second
  // is current; the map draws the third after it. A client still naming
  // the first is told to refresh, not answered for it.
  [Fact]
  public async Task PassedWorkNeedingReviewIsBehindTheTruck()
  {
    await using var f = await Fixture.CreateAsync();
    var passed = f.Add(1, day: 0, status: "in_transit");
    var current = f.Add(2, day: 1);
    var next = f.Add(3, day: 2);
    f.Db.DispatchSourceLinks.Add(
      new DispatchSourceLink
      {
        Provider = "source",
        ExternalId = "1",
        DispatchId = passed.Id,
        Dispatch = passed,
        ExecutionReviewReason = "Review the initial assignment.",
      }
    );
    await f.SaveAsync();
    await f.SavePassedPlanAsync(passed);

    var routes = await f.NextAsync(current);
    var stale = await f.HandleAsync(passed);

    Assert.Equal([next.Id], routes.Select(x => x.Id));
    Assert.False(stale.Success);
    Assert.Equal(409, stale.StatusCode);
  }

  // The client names work the inputs do not. Inputs captured within the
  // last five seconds by the owner's clock are trusted - a client polling
  // with a stale value costs no capture - and older inputs are captured
  // once more before the client is refused; the requests right after that
  // capture cost nothing until it is five seconds old in turn.
  [Fact]
  public async Task AStaleClientCurrentCostsAtMostOneCapturePerFiveSeconds()
  {
    await using var f = await Fixture.CreateAsync();
    var current = f.Add(1, day: 0, status: "in_transit");
    var other = f.Add(2, day: 1);
    await f.SaveAsync();
    await f.NextAsync(current);

    var fresh = await f.CapturesAsync(() => f.HandleAsync(other));
    f.Clock.Advance(TimeSpan.FromSeconds(10));
    var old = await f.CapturesAsync(() => f.HandleAsync(other));
    var after = new List<int>();
    for (var i = 0; i < 3; i++)
    {
      f.Clock.Advance(TimeSpan.FromSeconds(1));
      after.Add(await f.CapturesAsync(() => f.HandleAsync(other)));
    }
    f.Clock.Advance(TimeSpan.FromSeconds(3));
    var later = await f.CapturesAsync(() => f.HandleAsync(other));

    Assert.Equal(0, fresh);
    Assert.Equal(1, old);
    Assert.Equal([0, 0, 0], after);
    Assert.Equal(1, later);
    Assert.Equal(409, (await f.HandleAsync(other)).StatusCode);
  }

  // Another truck's work between this truck's loads is not this truck's.
  [Fact]
  public async Task AnotherTrucksWorkIsNotFollowed()
  {
    await using var f = await Fixture.CreateAsync();
    var peer = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "peer",
      UnitNumber = "Peer",
      IsActive = true,
    };
    f.Db.Trucks.Add(peer);
    var current = f.Add(1, day: 0, status: "in_transit");
    var next = f.Add(3, day: 2);
    f.Add(2, day: 1, truck: peer.Id);
    await f.SaveAsync();

    var routes = await f.NextAsync(current);

    Assert.Equal([next.Id], routes.Select(x => x.Id));
  }

  // One capture serves the first request; the next, and the summary
  // reader asking about the same truck, capture nothing.
  [Fact]
  public async Task ColdCapturesOnceAndWarmReadersCaptureNothing()
  {
    await using var f = await Fixture.CreateAsync();
    var current = f.Add(1, day: 0, status: "in_transit");
    f.Add(2, day: 1);
    await f.SaveAsync();

    f.Probe.Reset();
    await f.NextAsync(current);
    var cold = f.Probe.Captures;
    f.Probe.Reset();
    await f.NextAsync(current);
    await f.Services.PlanningInputs.ReadAsync(f.Truck.Id, default);
    var warm = f.Probe.Captures;

    Assert.Equal((1, 0), (cold, warm));
  }

  // Overlap, with a handshake rather than luck: while the first reader
  // holds the load gate, capturing, a second reader is started and the
  // first waits until the second has reached that gate. Cold, the second
  // then finds the first's capture; stale, the second sees the entry it
  // found is gone and drops nothing more. One capture each time.
  [Fact]
  public async Task ReadersMeetingAtTheGateShareOneCapture()
  {
    await using var f = await Fixture.CreateAsync();
    var first = f.Add(1, day: 0, status: "in_transit");
    var second = f.Add(2, day: 1);
    await f.SaveAsync();
    await using var peer = await f.PeerAsync();
    var truck = f.Truck.Id;

    var (coldFirst, coldSecond, cold) = await f.MeetAtGateAsync(
      () => f.Services.PlanningInputs.ReadAsync(truck, default),
      () => peer.Services.PlanningInputs.ReadAsync(truck, default)
    );
    var seen = f.Services.PlanningInputs.Version(truck);
    await f.SavePassedPlanAsync(first);
    var (staleFirst, staleSecond, stale) = await f.MeetAtGateAsync(
      () => f.Services.PlanningInputs.ReadAgainAsync(truck, seen, default),
      () => peer.Services.PlanningInputs.ReadAgainAsync(truck, seen, default)
    );

    Assert.Equal((1, 1), (cold, stale));
    Assert.Equal(first.Id, coldFirst?.CurrentWork?.DispatchId);
    Assert.Equal(first.Id, coldSecond?.CurrentWork?.DispatchId);
    Assert.Equal(second.Id, staleFirst?.CurrentWork?.DispatchId);
    Assert.Equal(second.Id, staleSecond?.CurrentWork?.DispatchId);
  }

  private sealed class Fixture(
    SqliteConnection keeper,
    string source,
    AppDbContext db,
    CaptureProbe probe,
    ReadCache reads,
    ManualTimeProvider clock,
    PlanningTestServices services
  ) : IAsyncDisposable
  {
    private DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public AppDbContext Db => db;
    public CaptureProbe Probe => probe;
    public PlanningTestServices Services => services;
    public ManualTimeProvider Clock => clock;
    public Truck Truck { get; } =
      new()
      {
        Id = Guid.NewGuid(),
        ExternalId = "11006",
        UnitNumber = "11006",
        IsActive = true,
      };

    public static async Task<Fixture> CreateAsync()
    {
      var source =
        $"Data Source=file:followers-{Guid.NewGuid():N}?mode=memory&cache=shared";
      var keeper = new SqliteConnection(source);
      await keeper.OpenAsync();
      var probe = new CaptureProbe();
      var reads = new ReadCache(Options.Create(new SynchronizationOptions()));
      var db = Context(source, probe);
      await db.Database.EnsureCreatedAsync();
      var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
      var fixture = new Fixture(
        keeper,
        source,
        db,
        probe,
        reads,
        clock,
        new PlanningTestServices(db, reads: reads, time: clock)
      );
      db.Trucks.Add(fixture.Truck);
      await db.SaveChangesAsync();
      return fixture;
    }

    private static AppDbContext Context(string source, CaptureProbe probe) =>
      new(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(source)
          .AddInterceptors(probe)
          .Options
      );

    // A second context over the same database and read cache: another
    // request running at the same time.
    public Task<Peer> PeerAsync()
    {
      var db = Context(source, probe);
      return Task.FromResult(
        new Peer(db, new PlanningTestServices(db, reads: reads, time: clock))
      );
    }

    public Load Add(
      int number,
      int day,
      string status = "assigned",
      Guid? id = null,
      bool dated = true,
      Guid? truck = null
    )
    {
      var date = Today.AddDays(day);
      var load = new Load
      {
        Id = id ?? Guid.NewGuid(),
        TruckId = truck ?? Truck.Id,
        LoadNumber = number,
        Status = status,
        ShipDate = date,
        DeliveryDate = date,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 1,
            Job = "Pick Up",
            Name = $"{number} pickup",
            Latitude = 40,
            Longitude = -80,
            ScheduledDate = dated ? date : null,
            ScheduledTime = dated ? new TimeOnly(8, 0) : null,
          },
          new()
          {
            Id = Guid.NewGuid(),
            Sequence = 2,
            Job = "Drop Off",
            Name = $"{number} delivery",
            Latitude = 41,
            Longitude = -80,
            ScheduledDate = dated ? date : null,
            ScheduledTime = dated ? new TimeOnly(16, 0) : null,
          },
        ],
      };
      db.Dispatches.Add(load);
      return load;
    }

    public async Task SaveAsync()
    {
      await db.SaveChangesAsync();
      reads.InvalidateItem("planning-inputs", Truck.Id);
    }

    // A saved route whose stops are all passed, written as another
    // process would: the row changes, the cached inputs do not.
    public async Task SavePassedPlanAsync(Load load)
    {
      var persisted = await db
        .Dispatches.AsNoTracking()
        .Include(x => x.Stops)
        .SingleAsync(x => x.Id == load.Id);
      var plan = new RoutePlan
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        TruckId = Truck.Id,
        Version = 1,
        CalculatedAt = DateTime.UtcNow,
        Tracking = new() { AllStopsPassed = true },
        Stops = persisted
          .Stops.OrderBy(x => x.Sequence)
          .Select(stop => new PlanStop(
            stop.Id,
            stop.Name,
            stop.Address,
            stop.Sequence,
            new((double)stop.Latitude!.Value, (double)stop.Longitude!.Value)
          ))
          .ToList(),
        Route = new()
        {
          Miles = 10,
          Legs = [new(10, 600, [new(40, -80), new(41, -80)])],
        },
      };
      db.DispatchRoutePlans.Add(
        new()
        {
          Id = plan.Id,
          DispatchId = load.Id,
          TruckId = Truck.Id,
          InputHash = RoutePlanInputs.Hash(persisted, plan.Profile),
          PlanJson = RoutePlanStorage.Serialize(plan),
        }
      );
      await db.SaveChangesAsync();
    }

    public Task<RequestResponse<NextLoadRoutesResponse>> HandleAsync(
      Load current
    ) =>
      new GetNextLoadRoutesHandler(
        new NextLoadRouteReader(db),
        services.PlanningInputs,
        services.DeadheadHistory,
        services.Routes,
        new SourceRoadDemand(new SourceRoadStore(db), TimeProvider.System)
      ).Handle(new(Truck.Id, current.Id), default);

    public async Task<IReadOnlyList<NextLoadRoute>> NextAsync(Load current)
    {
      var response = await HandleAsync(current);
      Assert.True(response.Success, string.Join("; ", response.Errors ?? []));
      return response.Response!.Routes ?? [];
    }

    public async Task<int> CapturesAsync<T>(Func<Task<T>> read)
    {
      probe.Reset();
      await read();
      return probe.Captures;
    }

    // Runs `first`; when it starts capturing - holding the load gate - it
    // starts `second` and waits until `second` is at that gate. Returns
    // both results and the captures they made together.
    public async Task<(T First, T Second, int Captures)> MeetAtGateAsync<T>(
      Func<Task<T>> first,
      Func<Task<T>> second
    )
    {
      probe.Reset();
      var arrived = new TaskCompletionSource(
        TaskCreationOptions.RunContinuationsAsynchronously
      );
      Task<T>? other = null;
      probe.OnCapture = async () =>
      {
        reads.WaitingForGate = family =>
        {
          if (family == "planning-inputs")
            arrived.TrySetResult();
        };
        other = Task.Run(second);
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        reads.WaitingForGate = null;
      };
      var mine = await first();
      Assert.True(arrived.Task.IsCompletedSuccessfully);
      return (mine, await other!, probe.Captures);
    }

    public async ValueTask DisposeAsync()
    {
      services.Dispose();
      reads.Dispose();
      await db.DisposeAsync();
      await keeper.DisposeAsync();
    }
  }

  private sealed class Peer(AppDbContext db, PlanningTestServices services)
    : IAsyncDisposable
  {
    public PlanningTestServices Services => services;

    public async ValueTask DisposeAsync()
    {
      services.Dispose();
      await db.DisposeAsync();
    }
  }

  // Captures of a truck's inputs: each reads the saved routes' metadata
  // exactly once. Counted across contexts running in parallel.
  private sealed class CaptureProbe : DbCommandInterceptor
  {
    private int captures;

    private Func<Task>? onCapture;

    // Run once, from the first capture, before its statement is sent.
    public Func<Task>? OnCapture
    {
      set => onCapture = value;
    }

    public int Captures => Volatile.Read(ref captures);

    public void Reset() => Interlocked.Exchange(ref captures, 0);

    public override async ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      if (command.CommandText.Contains("'storedAssignmentRevision'"))
      {
        Interlocked.Increment(ref captures);
        if (Interlocked.Exchange(ref onCapture, null) is { } hook)
          await hook();
      }
      return result;
    }
  }
}
