using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Features.Routing.Models;
using Domain.Entities.Fuel;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TruckFuelPlanScopeStoreTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task NativeThenLegacyScopesRoundTripWithTerminalOwnership(
    bool onward
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var expected = NativeSnapshot(fixture.Snapshot(), onward);
    Assert.True(
      await new TruckFuelPlanStore(fixture.Db).SaveAsync(expected, default)
    );
    await using var reopened = new AppDbContext(fixture.Options);
    var store = new TruckFuelPlanStore(reopened);
    foreach (var geometry in new[] { false, true })
    {
      var saved = Assert.IsType<TruckFuelPlanSnapshot>(
        await store.ReadAsync(fixture.TruckId, geometry, default)
      );
      Assert.Equal(expected.Stops, saved.Stops);
      Assert.Equal(expected.RootExecutionLegId, saved.RootExecutionLegId);
      Assert.Equal(expected.AssignmentRevision, saved.AssignmentRevision);
      Assert.Equal(
        expected.Plan.ArrivalPolicy!.NextDispatchId,
        saved.Plan.ArrivalPolicy!.NextDispatchId
      );
      Assert.Null(saved.Stops[^1].ExecutionLegId);
      Assert.Equal(0, saved.Stops[^1].AssignmentRevision);
    }
  }

  [Theory]
  [InlineData("root-leg")]
  [InlineData("root-revision")]
  [InlineData("future-root-leg")]
  [InlineData("future-other-leg")]
  [InlineData("future-revision")]
  [InlineData("legacy-root-revision")]
  [InlineData("missing-signature")]
  [InlineData("empty-signature")]
  [InlineData("terminal-in-itinerary")]
  [InlineData("terminal-missing-signature")]
  [InlineData("terminal-empty-id")]
  [InlineData("terminal-native")]
  public async Task InvalidScopeCannotReplaceOrBeReadAsAValidSnapshot(
    string corruption
  )
  {
    await using var fixture = await TruckFuelPlanFixture.CreateAsync();
    var store = new TruckFuelPlanStore(fixture.Db);
    var original = NativeSnapshot(fixture.Snapshot(), true);
    Assert.True(await store.SaveAsync(original, default));
    var row = await fixture.Db.Set<TruckFuelPlan>().SingleAsync();
    var summary = JsonNode.Parse(row.SummaryJson)!;
    var plan = summary["plan"]!;
    var first = summary["stops"]![0]!;
    var last = summary["stops"]![1]!;
    var terminal = plan["arrivalPolicy"]!;
    switch (corruption)
    {
      case "root-leg":
        first["executionLegId"] = Guid.NewGuid();
        break;
      case "root-revision":
        first["assignmentRevision"] = 8;
        break;
      case "future-root-leg":
        last["executionLegId"] = original.RootExecutionLegId;
        last["assignmentRevision"] = original.AssignmentRevision;
        break;
      case "future-other-leg":
        last["executionLegId"] = Guid.NewGuid();
        break;
      case "future-revision":
        last["assignmentRevision"] = 7;
        break;
      case "legacy-root-revision":
        summary["rootExecutionLegId"] = null;
        plan["executionLegId"] = null;
        first["executionLegId"] = null;
        break;
      case "missing-signature":
        plan["dispatchSignatures"]!
          .AsObject()
          .Remove(fixture.FutureId.ToString());
        break;
      case "empty-signature":
        plan["dispatchSignatures"]![fixture.FutureId.ToString()] = " ";
        break;
      case "terminal-in-itinerary":
        terminal["nextDispatchId"] = fixture.FutureId;
        break;
      case "terminal-missing-signature":
        terminal["nextDispatchId"] = Guid.NewGuid();
        break;
      case "terminal-empty-id":
        terminal["nextDispatchId"] = Guid.Empty;
        break;
      case "terminal-native":
        summary["stops"]!.AsArray().RemoveAt(1);
        plan["dispatchIds"]!.AsArray().RemoveAt(1);
        plan["stops"] = new JsonArray();
        break;
    }
    row.SummaryJson = summary.ToJsonString();
    await fixture.Db.SaveChangesAsync();
    Assert.Null(await store.ReadAsync(fixture.TruckId, false, default));
    Assert.Null(await store.ReadAsync(fixture.TruckId, true, default));
    var invalid = summary.Deserialize<TruckFuelPlanSnapshot>(
      new JsonSerializerOptions(JsonSerializerDefaults.Web)
    )! with
    {
      CheckedRoute = original.CheckedRoute,
    };
    await Assert.ThrowsAsync<ArgumentException>(
      () => store.SaveAsync(invalid, default)
    );
    Assert.Equal(
      row.SummaryJson,
      (
        await fixture.Db.Set<TruckFuelPlan>().AsNoTracking().SingleAsync()
      ).SummaryJson
    );
  }

  private static TruckFuelPlanSnapshot NativeSnapshot(
    TruckFuelPlanSnapshot snapshot,
    bool onward
  )
  {
    var leg = Guid.NewGuid();
    snapshot.Plan.ExecutionLegId = leg;
    snapshot.Plan.AssignmentRevision = 7;
    foreach (var id in snapshot.Plan.DispatchIds)
      snapshot.Plan.DispatchSignatures[id] = $"signature-{id}";
    var next = onward ? Guid.NewGuid() : (Guid?)null;
    if (next.HasValue)
      snapshot.Plan.DispatchSignatures[next.Value] = "onward-signature";
    snapshot.Plan.ArrivalPolicy = new()
    {
      MinimumGallons = 125,
      TargetGallons = 125,
      PoorArea = true,
      EconomicPurchasesOnly = true,
      NextDispatchId = next,
    };
    return snapshot with
    {
      RootExecutionLegId = leg,
      AssignmentRevision = 7,
      Stops =
      [
        snapshot.Stops[0] with
        {
          ExecutionLegId = leg,
          AssignmentRevision = 7,
        },
        snapshot.Stops[1],
      ],
    };
  }
}
