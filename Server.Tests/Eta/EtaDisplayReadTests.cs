using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Eta;
using Domain.Models.Routing;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Eta;

// Stage 4c of docs/architecture/current-work.md: a prepared planning
// summary shows the forecast as it stands now, read from memory with no
// side effects (EtaService.PeekForDisplay), against the prepared plan's
// own identity and version - not its display-trimmed geometry.
[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaDisplayReadTests
{
  [Fact]
  public void ATrimmedPlanReadsTheForecastRecordedForTheFullOne()
  {
    using var fixture = new Fixture();
    var services = fixture.Services;
    var full = State(Plan());
    var forecast = Forecast();
    services.Eta.Record(full, "signature", forecast);

    var shown = services.Eta.PeekForDisplay(Trimmed(full));

    Assert.Equal(forecast, shown);
  }

  [Fact]
  public void ADisplayReadChangesNothing()
  {
    using var fixture = new Fixture();
    var services = fixture.Services;
    var state = State(Plan(leg: Guid.NewGuid()));
    services.Eta.Record(state, "signature", Forecast());
    var memory = services.EtaMemory;
    var results = memory.Results.ToArray();

    services.Eta.PeekForDisplay(state);
    services.Eta.PeekForDisplay(state with { Plan = Other(state.Plan!) });

    Assert.Empty(memory.Viewed);
    Assert.Equal(results, memory.Results.ToArray());
  }

  // The same work on a road that moved since - tracking passed a stop -
  // shows the forecast marked updating; other work shows none, and the
  // forecast stays for the work it belongs to.
  [Fact]
  public void AMovedRoadIsUpdatingAndOtherWorkShowsNone()
  {
    using var fixture = new Fixture();
    var services = fixture.Services;
    var state = State(Plan());
    services.Eta.Record(state, "signature", Forecast());
    var moved = Clone(state.Plan!);
    moved.Tracking.NextStopId = moved.Stops[^1].Id;

    var updating = services.Eta.PeekForDisplay(state with { Plan = moved });
    var other = services.Eta.PeekForDisplay(
      state with
      {
        Plan = Other(state.Plan!),
      }
    );

    Assert.True(updating?.RouteUpdatePending);
    Assert.Null(other);
    Assert.False(services.Eta.PeekForDisplay(state)!.RouteUpdatePending);
  }

  private sealed class Fixture : IDisposable
  {
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;

    public Fixture()
    {
      connection.Open();
      db = new(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .Options
      );
      Services = new(db);
    }

    public PlanningTestServices Services { get; }

    public void Dispose()
    {
      Services.Dispose();
      db.Dispose();
      connection.Dispose();
    }
  }

  private static RoutePlan Plan(Guid? leg = null)
  {
    var stops = new[] { Guid.NewGuid(), Guid.NewGuid() };
    return new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = Guid.NewGuid(),
      ExecutionLegId = leg,
      AssignmentRevision = leg is null ? 0 : 3,
      TruckId = Guid.NewGuid(),
      Version = 4,
      CalculatedAt = DateTime.UtcNow,
      Tracking = new()
      {
        NextStopId = stops[0],
        LastObservationAt = DateTime.UtcNow,
      },
      Stops =
      [
        new(stops[0], "Pickup", "1 Main", 1, new(40, -80)),
        new(stops[1], "Delivery", "2 Main", 2, new(41, -80)),
      ],
      Route = new()
      {
        Miles = 100,
        Legs =
        [
          new(
            100,
            6000,
            [
              .. Enumerable
                .Range(0, 400)
                .Select(i => new RoutePoint(40 + i / 400d, -80)),
            ]
          ),
        ],
      },
    };
  }

  private static RoutePlanningState State(RoutePlan plan) =>
    new(new(), plan, null, null, null, true);

  // The plan as a prepared summary keeps it: copied and trimmed for display.
  private static RoutePlanningState Trimmed(RoutePlanningState state)
  {
    var plan = Clone(state.Plan!);
    PlanningReadService.TrimForDisplay(plan, plan.Id, plan.Version);
    return state with { Plan = plan };
  }

  private static RoutePlan Other(RoutePlan plan)
  {
    var other = Clone(plan);
    other.TruckId = Guid.NewGuid();
    return other;
  }

  private static RoutePlan Clone(RoutePlan plan) =>
    JsonSerializer.Deserialize<RoutePlan>(
      JsonSerializer.Serialize(plan, RoutingJson.Options),
      RoutingJson.Options
    )!;

  private static DispatchEta Forecast() =>
    new(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), [], null, []);
}
