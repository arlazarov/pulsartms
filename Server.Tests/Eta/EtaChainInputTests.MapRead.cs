using System.Text.Json;
using Application.Features.Execution.Models;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Server.Tests.Eta;

// The map's display read and the ETA worker describe the same load, so the
// forecast the worker publishes is the map's as current, whatever the
// stops carry (names, notes, commodity, appointment time zone). Added
// while diagnosing the 11007 map dash (docs/archive/2026-09/
// eta-11007-2026-09-25.md); it did not reproduce the dash.
public sealed partial class EtaChainInputTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task TheMapReadFindsTheForecastTheWorkerMade(bool detailed)
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    if (detailed)
    {
      await f
        .Db.Set<DispatchStop>()
        .ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.Commodity, "Paper goods")
            .SetProperty(x => x.Notes, "Check in at gate 3")
            .SetProperty(x => x.Name, "Target DC 3802")
            .SetProperty(x => x.AppointmentTimeZoneId, "America/New_York")
        );
      f.Db.ChangeTracker.Clear();
      f.Services.Reads.Invalidate("dispatch");
    }
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);

    var work = (
      await f.Services.PlanningInputs.ReadAsync(f.Truck.Id, default)
    )!;
    var segment = PlanningWorkPolicy.Candidates(work.Itinerary).First();
    var mapLoad = PlanningWorkPolicy.Resolve(work.Itinerary, segment);
    var etaLoad = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!.Loads[0];
    var diff = mapLoad
      .Stops.Zip(etaLoad.Stops)
      .Select(p =>
        (
          Map: JsonSerializer.Serialize(p.First),
          Eta: JsonSerializer.Serialize(p.Second)
        )
      )
      .Where(p => p.Map != p.Eta)
      .ToList();
    var state = await f.Services.Routes.GetAsync(
      mapLoad,
      default,
      displayOnly: true
    );

    Assert.Empty(diff);
    var cached = f.Services.Eta.GetCached(state);
    Assert.NotNull(cached);
    Assert.False(cached.RouteUpdatePending);
  }

  // The shapes of 11007 and 11005, against the same authoritative work: an
  // accepted execution leg with its pickup done and its road saved for the
  // leg; the same leg after the load's stops were edited (notes, commodity,
  // a second appointment) so the leg's captured stops and the live ones
  // differ; and a load with every stop field filled; and truck 54777's
  // (AMF1409, September 25): the source stop's address edited after the
  // leg captured it. The worker records a
  // forecast, then the map's display read and the board's metadata read
  // ask for it. A forecast judged
  // "other work" is removed and read as none; the same work reads current
  // or updating. The entry is checked first, so "none" can only mean the
  // read judged the work different.
  [Theory]
  [InlineData("accepted leg, pickup done")]
  [InlineData("accepted leg, stops edited after acceptance")]
  [InlineData("no leg, every stop field filled")]
  [InlineData("accepted leg, stop address edited after acceptance")]
  public async Task TheMapReadKeepsTheWorkersForecastForIncidentShapedWork(
    string shape
  )
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    ExecutionLeg? leg = null;
    if (shape.StartsWith("accepted leg", StringComparison.Ordinal))
    {
      var pickup = await f
        .Db.Set<DispatchStop>()
        .SingleAsync(x => x.Id == f.Current.Stops[0].Id);
      pickup.DepartedAt = DateTime.UtcNow.AddHours(-1);
      await f.Db.SaveChangesAsync();
      var captured = await f
        .Db.Set<DispatchStop>()
        .AsNoTracking()
        .Where(x => x.DispatchId == f.Current.Id)
        .OrderBy(x => x.Sequence)
        .ToListAsync();
      leg = new ExecutionLeg
      {
        Id = Guid.NewGuid(),
        Trip = new Trip { Id = Guid.NewGuid() },
        TruckId = f.Truck.Id,
        Status = "active",
        Revision = 3,
        Stops = ExecutionStopRows.Capture(captured),
        Loads =
        [
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = f.Current.Id,
            StartVisitId = captured[0].Id,
            EndVisitId = captured[^1].Id,
          },
        ],
      };
      f.Db.ExecutionLegs.Add(leg);
      await f.Db.SaveChangesAsync();
      // The road the planner saves for the leg: the load's road, now owned
      // by the leg at its revision, with the leg's own input hash.
      var row = await f.Db.DispatchRoutePlans.SingleAsync(x =>
        x.DispatchId == f.Current.Id
      );
      var plan = JsonSerializer.Deserialize<RoutePlan>(
        row.PlanJson,
        RoutingJson.Options
      )!;
      plan.ExecutionLegId = leg.Id;
      plan.AssignmentRevision = leg.Revision;
      var snapshot = await f.Services.Routes.LoadAsync(
        f.Current.Id,
        default,
        leg.Id,
        f.Truck.Id
      );
      row.ExecutionLegId = leg.Id;
      row.AssignmentRevision = leg.Revision;
      row.InputHash = RoutePlanInputs.Hash(snapshot, plan.Profile);
      row.PlanJson = JsonSerializer.Serialize(plan, RoutingJson.Options);
      await f.Db.SaveChangesAsync();
    }
    if (shape.Contains("address", StringComparison.Ordinal))
      await f
        .Db.Set<DispatchStop>()
        .Where(x => x.DispatchId == f.Current.Id)
        .ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.Address, x => x.Address + " Suite 2")
        );
    else if (shape != "accepted leg, pickup done")
      await f
        .Db.Set<DispatchStop>()
        .Where(x => x.DispatchId == f.Current.Id)
        .ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.Notes, "Check in at gate 3")
            .SetProperty(x => x.Commodity, "Paper goods")
            .SetProperty(x => x.Name, "Target DC 3802")
            .SetProperty(x => x.AppointmentTimeZoneId, "America/New_York")
            .SetProperty(
              x => x.ScheduledDate2,
              DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))
            )
            .SetProperty(x => x.ScheduledTime2, new TimeOnly(18, 0))
        );
    f.Db.ChangeTracker.Clear();
    f.Services.Reads.Invalidate("dispatch");

    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);

    var work = (
      await f.Services.PlanningInputs.ReadAsync(f.Truck.Id, default)
    )!;
    var segment = PlanningWorkPolicy.Candidates(work.Itinerary).First();
    var mapLoad = PlanningWorkPolicy.Resolve(work.Itinerary, segment);
    var etaLoad = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!.Loads[0];
    Assert.Equal(leg?.Id, mapLoad.ExecutionLegId);
    Assert.Equal(leg?.Id, etaLoad.ExecutionLegId);
    Assert.Equal(
      JsonSerializer.Serialize(etaLoad.Stops),
      JsonSerializer.Serialize(mapLoad.Stops)
    );
    var scope = f.Services.EtaMemory.Scope(f.Current.Id, leg?.Id);
    Assert.True(
      f.Services.EtaMemory.Results.ContainsKey(scope),
      "The worker recorded no forecast for this work."
    );
    var state = await f.Services.Routes.GetAsync(
      mapLoad,
      default,
      cachedTelemetryOnly: true,
      displayOnly: true
    );

    // The board's summary reads the same load for metadata only
    // (PlanningReadService.ReadBoardWorkAsync); it is judged the same way.
    var metadata = await f.Services.Routes.GetAsync(
      mapLoad,
      default,
      cachedTelemetryOnly: true,
      displayOnly: true,
      metadataOnly: true
    );

    var cached = f.Services.Eta.GetCached(state);
    var board = f.Services.Eta.GetCached(metadata);

    Assert.NotNull(cached);
    Assert.NotNull(board);
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(scope));

    // What the map card reads is the planning summary: the background
    // prepares the truck (AutomaticPlanningService), and the publisher
    // copies the result through JSON and attaches the ETA from memory
    // (PlanningSummaryPublisher). That copy must find the same forecast,
    // and the memory notes the answer the publisher logs.
    using var cache = new MemoryCache(new MemoryCacheOptions());
    var synchronization = Options.Create(new SynchronizationOptions());
    var planned = await new AutomaticPlanningService(
      f.Services.Routes,
      f.Services.Fuel,
      f.Services.PlanningInputs,
      cache,
      new PlanningReadService(
        f.Services.Routes,
        f.Services.PlanningInputs,
        f.Services.Refreshes(cache, synchronization),
        f.Services.Sender,
        synchronization,
        f.Services.Eta,
        f.Services.FuelPlans
      )
    ).ForTruckAsync(f.Truck.Id, default);
    var copy = JsonSerializer.Deserialize<AutomaticPlanningResult>(
      JsonSerializer.SerializeToUtf8Bytes(planned, RoutingJson.Options),
      RoutingJson.Options
    )!;

    Assert.NotNull(f.Services.Eta.GetCached(copy.State!));
    Assert.Contains(
      f.Services.EtaMemory.MapAnswer(scope),
      new[] { "current", "updating" }
    );
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(scope));
  }
}
