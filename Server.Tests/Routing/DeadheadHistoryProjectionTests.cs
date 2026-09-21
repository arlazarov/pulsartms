using System.Collections.Immutable;
using System.Text.Json;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Execution;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Finance")]
[Trait("Kind", "Unit")]
public sealed class DeadheadHistoryProjectionTests
{
  [Fact]
  public void AcceptedRoadFactsUseOnlyAcceptedAssignmentAndDetachedVisits()
  {
    var source = Make(Guid.NewGuid(), 5);
    source.PlanningTruckId = Guid.NewGuid();
    source.PlanningFromStopId = source.Stops[0].Id;
    source.PlanningAssignmentRevision = 17;
    var accepted = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      Revision = 3,
      RouteChoiceRevision = 2,
      Status = "active",
    };
    var work = RouteWorkProjection.Capture(source, accepted, source.Stops);
    var signature = BaseRouteService.Signature(work, new());
    source.Stops[0].Latitude = 50;
    source.Stops.Clear();
    accepted.Revision++;
    accepted.TruckId = Guid.NewGuid();

    Assert.Equal(3, work.AssignmentRevision);
    Assert.NotEqual(accepted.TruckId, work.TruckId);
    Assert.Null(work.PlanningTruckId);
    Assert.Null(work.PlanningFromStopId);
    Assert.Equal(0, work.PlanningAssignmentRevision);
    Assert.Equal(40, work.Stops[0].Latitude);
    Assert.Equal(signature, BaseRouteService.Signature(work, new()));
  }

  [Fact]
  public void CapturedFactsAndCompatibilityCopiesDoNotShareMutableState()
  {
    var truck = Guid.NewGuid();
    var current = Make(truck, 5);
    var previous = Make(truck, 1);
    var candidates = new List<Load> { previous };
    var snapshot = DeadheadHistoryProjection.Capture(
      Source(current, candidates, false)
    );
    var signature = snapshot.InputSignature;
    current.Price = 5000;
    current.Stops[0].City = "Changed";
    previous.Stops[^1].Longitude = -70;
    candidates.Clear();

    var connection = DeadheadConnection.Find(snapshot)!;
    Assert.Equal(1000m, connection.Current.Price);
    Assert.Equal("", connection.To.City);
    Assert.Equal(-80m, connection.From.Longitude);
    var edited = connection with
    {
      Current = connection.Current with { Stops = [] },
      From = connection.From with { Longitude = -60 },
    };
    Assert.Empty(edited.Current.Stops);
    Assert.Equal(-60, edited.From.Longitude);
    Assert.Equal(2, DeadheadConnection.Find(snapshot)!.Current.Stops.Length);
    Assert.Equal(-80m, DeadheadConnection.Find(snapshot)!.From.Longitude);
    Assert.Equal(signature, snapshot.InputSignature);
  }

  [Fact]
  public void CandidateEnumerationOrderDoesNotChangeContentSignature()
  {
    var truck = Guid.NewGuid();
    var current = Make(truck, 5);
    var first = Make(truck, 1);
    var second = Make(truck, 3);
    var a = DeadheadHistoryProjection.Capture(
      Source(current, [first, second], false)
    );
    var b = DeadheadHistoryProjection.Capture(
      Source(current, [second, first], false)
    );

    Assert.Equal(a.InputSignature, b.InputSignature);
    Assert.Equal(second.Id, DeadheadConnection.Find(b)!.Previous.Id);
  }

  [Theory]
  [InlineData("price")]
  [InlineData("schedule")]
  [InlineData("assignment")]
  [InlineData("completion")]
  [InlineData("unknown")]
  public void HistoryTokenIncludesInputsOutsideTheGeometryHash(string change)
  {
    var truck = Guid.NewGuid();
    var current = Make(truck, 5);
    var previous = Make(truck, 1);
    var before = DeadheadHistoryProjection.Capture(
      Source(current, [previous], false)
    );
    if (change == "price")
      current.Price++;
    if (change == "schedule")
      previous.Stops[^1].ScheduledTime = new(12, 0);
    if (change == "assignment")
      previous.PlanningAssignmentRevision++;
    if (change == "completion")
      previous.Stops[^1].ManualCompletionRevision++;
    var after = DeadheadHistoryProjection.Capture(
      Source(current, [previous], change == "unknown")
    );

    Assert.NotEqual(before.InputSignature, after.InputSignature);
    if (change == "unknown")
      Assert.Null(DeadheadConnection.Find(after));
    else
      Assert.Equal(
        DeadheadConnection.Find(before)!.Signature(new TruckRouteProfile()),
        DeadheadConnection.Find(after)!.Signature(new TruckRouteProfile())
      );
  }

  [Fact]
  public void NativeCompletionAndHandoffFactsSurvivePersistedSnapshot()
  {
    var source = Make(Guid.NewGuid(), 1);
    source.ExecutionLegId = Guid.NewGuid();
    source.ExecutionStatus = "completed";
    source.AssignmentRevision = 7;
    source.Stops[0].AwaitingHandoff = true;
    source.Stops[^1].ExecutionCompleted = true;
    source.Stops[^1].ManualAction = "Delivery";
    source.Stops[^1].ManualStateAfter = "Empty";
    source.Stops[^1].StateAfter = "Empty";
    source.Stops[^1].CompletionOverride = false;
    source.Stops[^1].SourceAddressJson = "{\"street\":\"captured\"}";
    var facts = RouteWorkProjection.Capture(source);

    var restored = JsonSerializer.Deserialize<RouteWorkSnapshot>(
      JsonSerializer.Serialize(facts)
    )!;
    Assert.Equal(facts, restored with { Stops = facts.Stops });
    Assert.Equal(facts.Stops.ToArray(), restored.Stops.ToArray());
    Assert.False(restored.Stops[^1].IsCompleted);
    Assert.True(
      (restored.Stops[^1] with { CompletionOverride = null }).IsCompleted
    );
    Assert.True(restored.Stops[0].AwaitingHandoff);
  }

  [Theory]
  [InlineData("accepted")]
  [InlineData("missing-anchor")]
  [InlineData("personal-gap")]
  [InlineData("conflicting-truck")]
  public void FrozenTruckPathRetainsManualStartAndRejectsBrokenContinuity(
    string scenario
  )
  {
    var truck = Guid.NewGuid();
    var source = Make(truck, 1);
    var start = source.Stops[0];
    start.ManualAction = "Collect truck";
    start.ManualStateAfter = "Empty";
    start.TruckId = truck;
    source.PlanningTruckId = truck;
    source.PlanningFromStopId = start.Id;
    source.Stops.Insert(
      0,
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 0,
        ManualAction = "Driver start",
        ManualStateAfter = "No truck",
      }
    );
    if (scenario == "missing-anchor")
      source.PlanningFromStopId = Guid.NewGuid();
    if (scenario == "personal-gap")
    {
      source.Stops[^1].ManualAction = "Driver start";
      source.Stops[^1].ManualStateAfter = "No truck";
    }
    if (scenario == "conflicting-truck")
      source.TruckId = Guid.NewGuid();
    var captured = RouteWorkProjection.Capture(source);
    source.Stops.Clear();

    var path = RouteWorkProjection.TruckItinerary(captured);

    Assert.Equal(3, captured.Stops.Length);
    if (scenario == "accepted")
    {
      Assert.Equal(2, path.Stops.Length);
      Assert.Equal(start.Id, path.Stops[0].Id);
      Assert.Equal("Collect truck", path.Stops[0].Job);
      Assert.Equal("Empty", path.Stops[0].StateAfter);
      Assert.Equal(truck, path.TruckId);
    }
    else
      Assert.Empty(path.Stops);
  }

  private static DeadheadHistorySource Source(
    Load current,
    IReadOnlyList<Load> previous,
    bool unknown
  ) =>
    new(
      RouteWorkProjection.Capture(current),
      previous.Select(RouteWorkProjection.Capture).ToImmutableArray(),
      unknown
    );

  private static Load Make(Guid truck, int day) =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckId = truck,
      Status = "assigned",
      Price = 1000,
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
          Longitude = -80,
        },
      ],
    };
}
