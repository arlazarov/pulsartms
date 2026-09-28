using Application.Features.Eta.Services;
using Domain.Models.Eta;
using Domain.Models.Routing;
using Domain.Policies;
using Infrastructure.Integrations.GeoTimeZone;
using Microsoft.Extensions.Options;
using Server.Tests.Support;

namespace Server.Tests.Eta;

// Root's review of b1401ddb: EtaService named a leg's scope (its identity)
// and then published or viewed it in a second call. The bound, trimming
// between the two, could forget the leg's identity and let the write
// recreate the scope without it, so the refresh worker would resolve the
// leg as a load. Reproduced at the real callers: memory is filled with
// scopes touched later than the leg, so the leg is the oldest when the
// bound trims right after it is named.
[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class EtaMemoryCallerTests
{
  private static readonly DateTimeOffset Start = new(
    2026,
    9,
    27,
    20,
    0,
    0,
    TimeSpan.Zero
  );

  [Fact]
  public void ARecordedLegKeepsItsIdentityOrIsForgottenWhole()
  {
    var (memory, eta, time) = Full();
    var state = LegState();
    var leg = state.Plan!.ExecutionLegId!.Value;

    eta.Record(state, "signature", Forecast());

    AssertWhole(memory, leg, state.Plan.DispatchId);
  }

  [Fact]
  public void AViewedLegKeepsItsIdentityOrIsForgottenWhole()
  {
    var (memory, eta, _) = Full();
    var state = LegState();
    var leg = state.Plan!.ExecutionLegId!.Value;

    eta.GetCached(state);

    AssertWhole(memory, leg, state.Plan.DispatchId);
  }

  // Each way a leg's scope is written names its load in the same step.
  [Theory]
  [InlineData("publish")]
  [InlineData("view")]
  [InlineData("demand")]
  [InlineData("map-answer")]
  public void EveryWriteOfALegNamesItsLoad(string write)
  {
    using var memory = new EtaMemory(new ManualTimeProvider(Start));
    var scope = new EtaMemory.ScopeIdentity(Guid.NewGuid(), Guid.NewGuid());
    var leg = scope.ExecutionLegId!.Value;
    var now = Start.UtcDateTime;

    switch (write)
    {
      case "publish":
        memory.Publish(scope, new("signature", Forecast()));
        break;
      case "view":
        memory.View(scope, now);
        break;
      case "demand":
        memory.Demand(scope, "inputs", now);
        break;
      default:
        memory.NoteMapAnswer(scope, "current");
        break;
    }

    Assert.Equal(scope, memory.Resolve(leg));
  }

  // Whatever the bound did, a leg scope that holds anything holds its
  // identity: the worker must never resolve the leg as a load.
  private static void AssertWhole(EtaMemory memory, Guid leg, Guid dispatch)
  {
    var holds =
      memory.Results.ContainsKey(leg)
      || memory.Viewed.ContainsKey(leg)
      || memory.Tracks(leg);
    if (holds)
      Assert.Equal(
        new EtaMemory.ScopeIdentity(dispatch, leg),
        memory.Resolve(leg)
      );
  }

  // A memory at its bound with scopes touched an hour after the clock the
  // leg is then touched at.
  private static (EtaMemory, EtaService, ManualTimeProvider) Full()
  {
    var time = new ManualTimeProvider(Start.AddHours(1));
    var memory = new EtaMemory(time);
    for (var i = 0; i < EtaMemory.MaximumScopes; i++)
      memory.Publish(Guid.NewGuid(), new("filler", Forecast()));
    time.UtcNow = Start;
    var hos = new PlanningTestServices.NoHos();
    var eta = new EtaService(
      null!,
      hos,
      new RouteRegionLookup(),
      memory,
      hos,
      Options.Create(new EtaPlanningOptions())
    );
    return (memory, eta, time);
  }

  private static RoutePlanningState LegState()
  {
    var stop = Guid.NewGuid();
    return new(
      new(),
      new RoutePlan
      {
        Id = Guid.NewGuid(),
        DispatchId = Guid.NewGuid(),
        ExecutionLegId = Guid.NewGuid(),
        AssignmentRevision = 3,
        TruckId = Guid.NewGuid(),
        Version = 1,
        Stops = [new(stop, "Delivery", "2 Main", 1, new(41, -80))],
      },
      null,
      null,
      null,
      true
    );
  }

  private static DispatchEta Forecast() =>
    new(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), [], null, []);
}
