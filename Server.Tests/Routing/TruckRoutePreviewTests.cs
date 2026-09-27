using System.Data.Common;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Dispatch.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Queries;
using Application.Features.Routing.Services.Routes;
using Application.Features.Synchronization.Options;
using Application.Interfaces;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Eta;
using Domain.Models.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Server.Tests.Routing;

using Application.Features.Execution.Models;
using Dispatch = global::Domain.Entities.Dispatch.Dispatch;

[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class TruckRoutePreviewTests
{
  [Theory]
  [InlineData(true, false, false)]
  [InlineData(false, false, false)]
  [InlineData(true, true, false)]
  [InlineData(true, false, true)]
  public async Task PreviewIncludesCachedProgressWithoutFetchingTelemetryOrAdvancingTracking(
    bool background,
    bool stale,
    bool otherTruck
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var stored = await fixture.SavePlanAsync(load);
    var originalJson = stored.PlanJson;
    var snapshot = new FleetLocationsResponse
    {
      Trucks =
      [
        new()
        {
          TruckId = otherTruck ? Guid.NewGuid() : fixture.Truck.Id,
          Latitude = 40.5m,
          Longitude = -80m,
          UpdatedAt = stale ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow,
        },
      ],
    };
    if (background)
      fixture.Telemetry.Set(snapshot);
    else
      await fixture.TelemetryCache.GetAsync(
        _ => Task.FromResult(snapshot),
        default
      );
    fixture.Probe.Start();
    var preview = await fixture.Preview.ForTruckAsync(
      fixture.Truck.Id,
      default
    );
    var state = Assert.IsType<RoutePlanningState>(preview.State);
    if (stale || otherTruck)
      Assert.Null(state.Progress?.ProgressMiles);
    else
    {
      Assert.Equal(5d, state.Progress!.ProgressMiles!.Value, 5);
      Assert.Equal(5d, state.Progress.RemainingMiles!.Value, 5);
    }
    Assert.Equal(originalJson, stored.PlanJson);
    Assert.Empty(state.Plan!.Tracking.PassedStopIds);
    Assert.Null(state.Eta);
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(0, fixture.Hos.Calls);
    Assert.Equal(0, fixture.Sender.TelemetryCalls);
  }

  [Theory]
  [InlineData("in_transit", false)]
  [InlineData("assigned", true)]
  public async Task OverdueCurrentLoadOwnsPreviewLiveReadAndEtaChainUntilDelivery(
    string status,
    bool pickedUp
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var current = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    current.Status = status;
    current.ShipDate = current.DeliveryDate = yesterday;
    current.Stops.ForEach(stop => stop.ScheduledDate = yesterday);
    if (pickedUp)
      current.Stops[0].PickedUpAt = DateTime.UtcNow.AddDays(-1);
    await fixture.Db.SaveChangesAsync();
    await fixture.SavePlanAsync(current);
    await fixture.SavePlanAsync(next);

    Assert.Equal(
      current.Id,
      (
        await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)
      ).DispatchId
    );
    Assert.Equal(
      current.Id,
      Assert.Single(await fixture.Preview.GetAsync(default)).DispatchId
    );
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var reader = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );
    Assert.Equal(
      current.Id,
      (await reader.ForTruckAsync(fixture.Truck.Id, default)).DispatchId
    );
    var chain = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    Assert.Equal(current.Id, chain!.RootDispatchId);
    Assert.Equal(
      new[] { current.Id, next.Id },
      chain.Loads.Select(load => load.Id)
    );

    current.Stops[^1].DeliveredAt = DateTime.UtcNow;
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("board");
    fixture.Services.Reads.Invalidate("dispatch");
    fixture.Services.Reads.InvalidateItem("planning-inputs", fixture.Truck.Id);
    Assert.Equal(
      next.Id,
      (
        await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)
      ).DispatchId
    );
    Assert.Equal(
      next.Id,
      (await reader.ForTruckAsync(fixture.Truck.Id, default)).DispatchId
    );
    Assert.Equal(
      next.Id,
      (
        await fixture.Services.EtaInputs.DescribeAsync(
          fixture.Truck.Id,
          default
        )
      )!.RootDispatchId
    );
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CurrentPollingOmitsOnlyKnownGeometryWhileBackgroundReadsKeepEveryCoordinate()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var entry = await fixture.SavePlanAsync(load);
    var stored = JsonSerializer.Deserialize<RoutePlan>(
      entry.PlanJson,
      RoutingJson.Options
    )!;
    stored.Route.Legs =
    [
      new(
        10,
        600,
        Enumerable
          .Range(0, 1001)
          .Select(i => new RoutePoint(40 + i / 1000d, -80))
          .ToList()
      ),
    ];
    entry.PlanJson = RoutePlanStorage.Serialize(stored);
    await fixture.Db.SaveChangesAsync();
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var reader = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );
    var initial = (await reader.ForDispatchAsync(load.Id, default))
      .State!
      .Plan!;
    Assert.False(initial.GeometryOmitted);
    Assert.Equal(2, initial.Route.Legs[0].Points.Count);
    load.Stops[0].Name = "Fresh pickup";
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("dispatch");
    fixture.Services.Reads.InvalidateItem("planning-inputs", fixture.Truck.Id);
    fixture.Probe.Start();
    var current = (
      await reader.ForDispatchAsync(load.Id, default, stored.Id, stored.Version)
    )
      .State!
      .Plan!;
    Assert.True(current.GeometryOmitted);
    Assert.Empty(current.Route.Legs[0].Points);
    Assert.Equal(
      "Fresh pickup",
      current.Stops.Single(x => x.Id == load.Stops[0].Id).Name
    );
    Assert.False(current.InputsChanged);
    var outdated = (
      await reader.ForDispatchAsync(
        load.Id,
        default,
        stored.Id,
        stored.Version - 1
      )
    )
      .State!
      .Plan!;
    Assert.False(outdated.GeometryOmitted);
    Assert.Equal(2, outdated.Route.Legs[0].Points.Count);
    var background = await fixture.Services.Routes.GetAsync(
      load.Id,
      default,
      cachedTelemetryOnly: true
    );
    Assert.False(background.Plan!.GeometryOmitted);
    Assert.Equal(1001, background.Plan.Route.Legs[0].Points.Count);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task KnownCompletedPlanCannotOmitTheNextCurrentDispatchGeometry()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var completed = await fixture.SavePlanAsync(first, completed: true);
    var current = await fixture.SavePlanAsync(next);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var reader = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );
    var changed = await reader.ForTruckAsync(
      fixture.Truck.Id,
      default,
      completed.Id,
      1
    );
    Assert.Equal(next.Id, changed.DispatchId);
    Assert.False(changed.State!.Plan!.GeometryOmitted);
    Assert.NotEmpty(changed.State.Plan.Route.Legs[0].Points);
    var known = await reader.ForTruckAsync(
      fixture.Truck.Id,
      default,
      current.Id,
      1
    );
    Assert.Equal(next.Id, known.DispatchId);
    Assert.True(known.State!.Plan!.GeometryOmitted);
    Assert.Empty(known.State.Plan.Route.Legs[0].Points);
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  public async Task MetadataDisplayReadsPreserveRecommendationFilteringWithChangedInputsAndExactGps(
    bool stale,
    bool offRoute
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var entry = await fixture.SavePlanAsync(load);
    var stored = JsonSerializer.Deserialize<RoutePlan>(
      entry.PlanJson,
      RoutingJson.Options
    )!;
    var profile = await fixture.Services.Routes.ProfileAsync(
      fixture.Truck.Id,
      default
    );
    stored.FuelRecommendations = new()
    {
      SettingsSignature = PlanningSettingsService.Signature(profile),
      Stations =
      [
        new() { Name = "Behind", RouteMile = 2 },
        new() { Name = "Ahead", RouteMile = 8 },
      ],
    };
    entry.PlanJson = RoutePlanStorage.Serialize(stored);
    load.Stops[0].Address = "Changed pickup address";
    await fixture.Db.SaveChangesAsync();
    fixture.Sender.AllowTelemetry = true;
    fixture.Sender.Telemetry = new()
    {
      Trucks =
      [
        new()
        {
          TruckId = fixture.Truck.Id,
          Latitude = 40.5m,
          Longitude = offRoute ? -79.97m : -80m,
          UpdatedAt = stale ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow,
        },
      ],
    };
    var resolved = await fixture.Services.Routes.LoadAsync(load.Id, default);
    var full = await fixture.Services.Routes.GetAsync(
      resolved,
      default,
      cachedTelemetryOnly: true
    );
    AutomaticPlanningService.ProjectRecommendations(full);
    var metadata = await fixture.Services.Routes.GetAsync(
      resolved,
      default,
      cachedTelemetryOnly: true,
      displayOnly: true,
      knownPlanId: stored.Id,
      knownVersion: stored.Version
    );
    Assert.True(metadata.Plan!.InputsChanged);
    Assert.True(metadata.Plan.GeometryOmitted);
    Assert.Empty(metadata.Plan.Route.Legs[0].Points);
    Assert.Equal(full.Progress, metadata.Progress);
    Assert.Equal(
      full.Plan!.FuelRecommendations!.Stations.Select(x => x.Name),
      metadata.Plan.FuelRecommendations!.Stations.Select(x => x.Name)
    );
    Assert.Equal(
      stale ? 2 : 1,
      metadata.Plan.FuelRecommendations.Stations.Count
    );
  }

  [Fact]
  public async Task UnstartedOverdueScopeStaysCompatibleWithLivePlanning()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    load.Status = "assigned";
    var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    load.ShipDate = load.DeliveryDate = yesterday;
    load.Stops.ForEach(stop => stop.ScheduledDate = yesterday);
    await fixture.Db.SaveChangesAsync();
    await fixture.SavePlanAsync(load);

    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);

    Assert.Null(result.DispatchId);
    Assert.Null(result.State);
    Assert.Empty(fixture.Sender.Requests);
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(0, fixture.Hos.Calls);
    Assert.False(fixture.Db.ChangeTracker.HasChanges());
  }

  [Fact]
  public async Task ColdPreviewReadsOnlySavedDataAndWarmPreviewReusesDispatchAndGeometryCaches()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var stored = await fixture.SavePlanAsync(load, fuel: true);
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(load.Id, result.DispatchId);
    var state = Assert.IsType<RoutePlanningState>(result.State);
    Assert.NotEmpty(state.Plan!.Route.Legs[0].Points);
    Assert.Empty(state.Plan.Route.Points);
    Assert.False(state.Plan.GeometryOmitted);
    Assert.Null(state.Plan.FuelPlan);
    Assert.Null(state.Plan.FuelRecommendations);
    Assert.Null(state.Progress);
    Assert.Null(state.FuelPercent);
    Assert.Null(state.Eta);
    Assert.Null(result.Hos);
    // Includes the batched completion proof for current crew selection.
    Assert.Equal(7, fixture.Probe.Reads);
    fixture.Probe.Start();
    state.Plan.Route.Legs.Clear();
    var repeated = await fixture.Preview.ForTruckAsync(
      fixture.Truck.Id,
      default
    );
    Assert.NotEmpty(repeated.State!.Plan!.Route.Legs);
    Assert.Equal(0, fixture.Probe.Reads);
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(0, fixture.Hos.Calls);
    Assert.Equal(0, fixture.Sender.TelemetryCalls);
    Assert.Empty(fixture.Sender.Requests);
    var fleet = Assert.Single(await fixture.Preview.GetAsync(default));
    Assert.Null(fleet.State!.Plan!.FuelPlan);
    Assert.Null(fleet.State.Plan.FuelRecommendations);
    Assert.Equal(
      stored.PlanJson,
      (
        await fixture.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
      ).PlanJson
    );
    Assert.False(fixture.Db.ChangeTracker.HasChanges());
  }

  // The fleet preview is kept once for everyone. A dispatcher looking at a
  // driver group that leaves this truck out may be the one whose request
  // fills it; it still holds the truck for the others.
  [Fact]
  public async Task ADispatchersDriverGroupNeverNarrowsTheSharedPreview()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    fixture.Scope.Scope = new(Guid.NewGuid(), "Elsewhere", [], []);

    var fleet = await fixture.Preview.GetAsync(default);

    Assert.Equal(load.Id, Assert.Single(fleet).DispatchId);
  }

  [Fact]
  public async Task FirstUnsavedLoadKeepsItsIdentityInsteadOfSelectingALaterSavedRoute()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var later = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(later);
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(first.Id, result.DispatchId);
    Assert.Null(result.State);
    Assert.Empty(await fixture.Preview.GetAsync(default));
  }

  [Fact]
  public async Task ExecutionGenerationInvalidatesCachedLegacyOwnershipProof()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var old = await fixture.Services.Routes.LoadAsync(load.Id, default);
    Assert.Null(old.ExecutionLegId);
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      Trip = trip,
      TruckId = fixture.Truck.Id,
      Status = "active",
      Revision = 1,
      Stops = ExecutionStopRows.Capture(load.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = load.Id,
          StartVisitId = load.Stops[0].Id,
          EndVisitId = load.Stops[^1].Id,
        },
      ],
    };
    fixture.Db.ExecutionLegs.Add(leg);
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("execution");
    fixture.Services.Reads.InvalidateItem("planning-inputs", fixture.Truck.Id);
    var current = await fixture.Services.Routes.LoadAsync(load.Id, default);
    Assert.Equal(leg.Id, current.ExecutionLegId);
    Assert.Equal(leg.Revision, current.AssignmentRevision);
  }

  [Fact]
  public async Task CompletedSavedLoadIsSkippedAndIdentityMatchesNormalPlanningRead()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(first, completed: true);
    await fixture.SavePlanAsync(next);
    fixture.Probe.Start();
    var preview = await fixture.Preview.ForTruckAsync(
      fixture.Truck.Id,
      default
    );
    Assert.Equal(next.Id, preview.DispatchId);
    Assert.Equal(0, fixture.Hos.Calls);
    Assert.Equal(0, fixture.Sender.TelemetryCalls);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var normal = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );
    var live = await normal.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(preview.DispatchId, live.DispatchId);
    Assert.Equal(0, fixture.Router.Calls);
  }

  // Stage 2 of docs/architecture/current-work.md: the inputs choose the
  // current work and every reader names that one - the warm summary, the
  // cold summary, the preview - with the same leg and revision.
  [Fact]
  public async Task EveryReaderNamesTheCurrentWorkTheInputsChose()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(first, completed: true);
    await fixture.SavePlanAsync(next);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var inputs = (
      await fixture.Services.PlanningInputs.ReadAsync(fixture.Truck.Id, default)
    )!;
    Assert.Equal(next.Id, inputs.CurrentWork?.DispatchId);

    var warm = await Normal(fixture).ForTruckAsync(fixture.Truck.Id, default);
    var cold = Summaries(fixture).Read(inputs);
    var preview = await fixture.Preview.ForTruckAsync(
      fixture.Truck.Id,
      default
    );

    foreach (var result in new[] { warm, cold, preview })
    {
      Assert.Equal(inputs.CurrentWork!.DispatchId, result.DispatchId);
      Assert.Equal(inputs.CurrentWork.ExecutionLegId, result.ExecutionLegId);
      Assert.Equal(inputs.CurrentAssignmentRevision, result.AssignmentRevision);
    }
  }

  // A passed load that now needs review is behind the truck: it used to be
  // resolved before its completion was checked, which refused the truck's
  // summary and dropped the truck from the previews.
  [Fact]
  public async Task APassedLoadNeedingReviewDoesNotRefuseTheCurrentWork()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(first, completed: true);
    await fixture.SavePlanAsync(next);
    fixture.Db.DispatchSourceLinks.Add(
      new DispatchSourceLink
      {
        Provider = "source",
        ExternalId = "1",
        DispatchId = first.Id,
        Dispatch = first,
        ExecutionReviewReason = "Review the initial assignment.",
      }
    );
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.InvalidateItem("planning-inputs", fixture.Truck.Id);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;

    var warm = await Normal(fixture).ForTruckAsync(fixture.Truck.Id, default);
    var preview = await fixture.Preview.ForTruckAsync(
      fixture.Truck.Id,
      default
    );

    Assert.Equal(next.Id, warm.DispatchId);
    Assert.NotNull(warm.State?.Plan);
    Assert.Equal(next.Id, preview.DispatchId);
    Assert.NotNull(preview.State?.Plan);
  }

  // Tracking passes the current work after the inputs were captured. A
  // reader built from that capture does not step on to the next load by
  // itself: it says the work changed, and the next capture decides.
  [Fact]
  public async Task TrackingThatPassesTheCurrentWorkMidReadIsAChange()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var entry = await fixture.SavePlanAsync(first);
    await fixture.SavePlanAsync(next);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var captured = (
      await fixture.Services.PlanningInputs.ReadFreshAsync(
        fixture.Truck.Id,
        default
      )
    )!;
    Assert.Equal(first.Id, captured.CurrentWork?.DispatchId);
    await PassAsync(fixture, entry, invalidateInputs: true);

    var changed = await Assert.ThrowsAsync<RoutePlanningException>(
      () => Normal(fixture).ForInputsAsync(captured, default)
    );
    Assert.True(changed.DependencyChanged);
    Assert.Equal(
      next.Id,
      (
        await Normal(fixture).ForTruckAsync(fixture.Truck.Id, default)
      ).DispatchId
    );
  }

  // The same change seen through inputs another process has not yet
  // invalidated: the reader captures once more through the owner - one
  // capture, not a loop, and not the reader stepping on by itself - and
  // the owner's entry is replaced, so the next read captures nothing.
  [Fact]
  public async Task AStaleCaptureIsReplacedOnceThroughTheOwner()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var entry = await fixture.SavePlanAsync(first);
    await fixture.SavePlanAsync(next);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var reader = Normal(fixture);
    Assert.Equal(
      first.Id,
      (await reader.ForTruckAsync(fixture.Truck.Id, default)).DispatchId
    );
    await PassAsync(fixture, entry, invalidateInputs: false);

    var stale = await CapturesOf(fixture, reader);
    var settled = await CapturesOf(fixture, reader);

    Assert.Equal((next.Id, 1), (stale.Result.DispatchId, stale.Captures));
    Assert.Equal((next.Id, 0), (settled.Result.DispatchId, settled.Captures));
  }

  // Two readers found the same stale entry. The first drops and recaptures
  // it; the second finds the entry already replaced and reads that - one
  // capture between them, not one each.
  [Fact]
  public async Task ReadersThatFoundTheSameStaleEntryShareOneCapture()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var entry = await fixture.SavePlanAsync(first);
    await fixture.SavePlanAsync(next);
    var inputs = fixture.Services.PlanningInputs;
    var truck = fixture.Truck.Id;
    var seen = inputs.Version(truck);
    await inputs.ReadAsync(truck, default, includeHos: false);
    await PassAsync(fixture, entry, invalidateInputs: false);

    fixture.Probe.Start();
    var a = await inputs.ReadAgainAsync(truck, seen, default, false);
    var b = await inputs.ReadAgainAsync(truck, seen, default, false);
    var captures = fixture.Probe.Statements.Count(x =>
      x.Contains("'storedAssignmentRevision'")
    );
    fixture.Probe.Stop();

    Assert.Equal(1, captures);
    Assert.Equal(next.Id, a?.CurrentWork?.DispatchId);
    Assert.Equal(next.Id, b?.CurrentWork?.DispatchId);
  }

  // Stage 2b: every consumer of the truck's work asks the same rule. The
  // first load's route is passed and it now needs review; the second is
  // current. The ETA root, automatic planning, the preview, the summary and
  // a writer's currency check all name the second - none is refused or cut
  // short by the passed load - and the reads each makes are counted cold,
  // warm and when two ask for the same thing in turn.
  [Fact]
  public async Task EveryConsumerOfTheSharedInputsFollowsTheOwner()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(first, completed: true);
    await fixture.SavePlanAsync(next);
    fixture.Db.DispatchSourceLinks.Add(
      new DispatchSourceLink
      {
        Provider = "source",
        ExternalId = "1",
        DispatchId = first.Id,
        Dispatch = first,
        ExecutionReviewReason = "Review the initial assignment.",
      }
    );
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.InvalidateItem("planning-inputs", fixture.Truck.Id);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var truck = fixture.Truck.Id;
    fixture.Sender.AllowLiveTelemetry = true;
    var automatic = new AutomaticPlanningService(
      fixture.Services.Routes,
      fixture.Services.Fuel,
      fixture.Services.PlanningInputs,
      fixture.Memory,
      Normal(fixture)
    );

    var owner = (
      await fixture.Services.PlanningInputs.ReadFreshAsync(truck, default)
    )!;
    var eta = await Measured(
      fixture,
      () => fixture.Services.EtaInputs.DescribeAsync(truck, default)
    );
    fixture.Probe.AllowWrites = true;
    var planned = await Measured(
      fixture,
      () => automatic.ForTruckAsync(truck, default)
    );
    fixture.Probe.AllowWrites = false;
    var preview = await Measured(
      fixture,
      () => fixture.Preview.ForTruckAsync(truck, default)
    );
    var summary = await Measured(
      fixture,
      () => Normal(fixture).ForTruckAsync(truck, default)
    );
    var itinerary = owner.Itinerary;
    var profile = (
      await fixture.Services.Profiles.GetManyAsync([truck], default)
    )[truck];
    bool Current(Dispatch load) =>
      PlanningCurrency
        .IsCurrentAsync(
          itinerary,
          RouteWorkProjection.Capture(
            itinerary.Segments.Single(x => x.Work.DispatchId == load.Id),
            itinerary.Resources.TruckNumber
          ),
          fixture.Services.RoutePlans,
          profile,
          default
        )
        .GetAwaiter()
        .GetResult();
    var cold = await Measured(fixture, () => Task.FromResult(Current(next)));
    var warm = await Measured(fixture, () => Task.FromResult(Current(next)));
    var passed = await Measured(fixture, () => Task.FromResult(Current(first)));

    Assert.Equal(next.Id, owner.CurrentWork?.DispatchId);
    Assert.Equal(next.Id, eta.Result?.RootDispatchId);
    Assert.Contains(
      new EtaWorkExclusion(
        new(first.Id, null),
        EtaWorkExclusionReason.SavedRouteCompleted
      ),
      eta.Result!.Exclusions
    );
    Assert.Equal(next.Id, planned.Result.DispatchId);
    Assert.Equal(next.Id, preview.Result.DispatchId);
    Assert.NotNull(preview.Result.State);
    Assert.Equal(next.Id, summary.Result.DispatchId);
    Assert.True(cold.Result);
    Assert.False(passed.Result);

    // ETA reads its own itinerary and saved roots in one batch.
    Assert.Equal(1, eta.Captures);
    // Automatic planning captures once; its writer's currency check then
    // reads the passed and the current plan's metadata one at a time -
    // the rows the capture read in its batch, again (a known repeat).
    Assert.Equal(3, planned.Captures);
    // Its writes dropped the cached inputs: the preview captures once, and
    // the summary after it reuses that capture.
    Assert.Equal(1, preview.Captures);
    Assert.Equal(0, summary.Captures);
    // The currency check reads only the plan that changed, then nothing.
    Assert.Equal((1, 0, 0), (cold.Reads, warm.Reads, passed.Reads));
  }

  // Stage 3a: the board says where each load stands, from the same inputs
  // the summary and the preview use. The first load's route is passed but
  // it is not delivered - earlier, with a conflict to show - the second is
  // current, the third next and the fourth upcoming, the same on a filtered
  // board. A load read at another assignment revision
  // than the inputs is stale, one they do not hold unknown. Once the
  // inputs are warm, the board captures nothing to say so.
  [Fact]
  public async Task TheBoardPlacesEachLoadByTheOwnersChoice()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var second = await fixture.AddAsync(2);
    var third = await fixture.AddAsync(3);
    var fourth = await fixture.AddAsync(4);
    await fixture.SavePlanAsync(first, completed: true);
    await fixture.SavePlanAsync(second);
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var truck = fixture.Truck.Id;
    var query = new GetDispatchBoardQuery(
      IncludeHos: false,
      IncludeFinancials: false,
      IncludeEta: false,
      Date: DateOnly.FromDateTime(DateTime.UtcNow)
    );
    async Task<List<DispatchResponse>> BoardAsync() =>
      Assert
        .Single(
          (await fixture.Sender.Board.Handle(query, default)).Response!.Items
        )
        .Dispatches;

    await fixture.Services.PlanningInputs.ReadAsync(truck, default);
    var (loads, _, captures) = await Measured(fixture, BoardAsync);
    var summary = await Normal(fixture).ForTruckAsync(truck, default);

    Assert.Equal(0, captures);
    Assert.Equal(
      [
        ("earlier", "route_passed_not_delivered"),
        ("current", null),
        ("next", null),
        ("upcoming", null),
      ],
      new[] { first, second, third, fourth }.Select(load =>
      {
        var row = loads.Single(x => x.Id == load.Id);
        return (row.WorkPhase, row.WorkConflict);
      })
    );
    Assert.Equal(
      summary.DispatchId,
      loads.Single(x => x.WorkPhase == "current").Id
    );

    // A filtered board keeps the truck's row whole - pages and searches
    // never split one truck's work - and places each load as before.
    var searched = Assert
      .Single(
        (
          await fixture.Sender.Board.Handle(
            query with
            {
              Search = "3",
            },
            default
          )
        )
          .Response!
          .Items
      )
      .Dispatches;
    Assert.Equal(
      loads.Select(x => (x.Id, x.WorkPhase)).OrderBy(x => x.Id),
      searched.Select(x => (x.Id, x.WorkPhase)).OrderBy(x => x.Id)
    );

    // The board reads rows fresh; the inputs are still the cached capture.
    // A row at another revision is stale, and a load the inputs do not
    // hold is unknown - neither is given a place.
    third.PlanningAssignmentRevision = 7;
    var fifth = await fixture.AddAsync(5);
    fixture.Services.Reads.Invalidate("board");
    var moved = await BoardAsync();
    Assert.Equal("stale", moved.Single(x => x.Id == third.Id).WorkPhase);
    Assert.Equal("unknown", moved.Single(x => x.Id == fifth.Id).WorkPhase);
    Assert.Equal("current", moved.Single(x => x.Id == second.Id).WorkPhase);
  }

  // One load handed from one truck to another stands under both on the
  // board, each row with its own leg. Each row is placed by its own
  // truck's inputs and its own leg - the outgoing truck's current work is
  // not the incoming truck's, and neither row borrows the other's place.
  [Fact]
  public async Task ATransferredLoadIsPlacedUnderEachTruckByItsOwnLeg()
  {
    await using var fixture = await Fixture.CreateAsync();
    var incoming = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "incoming",
      UnitNumber = "Incoming",
      IsActive = true,
    };
    fixture.Db.Trucks.Add(incoming);
    var load = await fixture.AddAsync(1);
    ExecutionLeg Leg(Guid truck, string status, long revision, int sequence) =>
      new()
      {
        Id = Guid.NewGuid(),
        Trip = new() { Id = Guid.NewGuid() },
        TruckId = truck,
        Status = status,
        Revision = revision,
        Stops = ExecutionStopRows.Capture(load.Stops),
        Loads =
        [
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = load.Id,
            Sequence = sequence,
            StartVisitId = load.Stops[0].Id,
            EndVisitId = load.Stops[^1].Id,
          },
        ],
      };
    var outgoing = Leg(fixture.Truck.Id, "active", 3, 0);
    var handed = Leg(incoming.Id, "planned", 1, 1);
    fixture.Db.ExecutionLegs.AddRange(outgoing, handed);
    await fixture.Db.SaveChangesAsync();
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;

    async Task<
      List<(Guid? Truck, Guid? Leg, string? Phase, string? Conflict)>
    > BoardAsync()
    {
      fixture.Services.Reads.Invalidate("board");
      return (
        await fixture.Sender.Board.Handle(
          new GetDispatchBoardQuery(
            IncludeHos: false,
            IncludeFinancials: false,
            IncludeEta: false,
            IncludePlanned: true,
            Date: DateOnly.FromDateTime(DateTime.UtcNow)
          ),
          default
        )
      )
        .Response!.Items.SelectMany(row =>
          row.Dispatches.Where(x => x.Id == load.Id)
            .Select(x =>
              (row.TruckId, x.ExecutionLegId, x.WorkPhase, x.WorkConflict)
            )
        )
        .OrderBy(x => x.ExecutionLegId == handed.Id)
        .ToList();
    }

    Assert.Equal(
      [
        (fixture.Truck.Id, outgoing.Id, "current", null),
        (incoming.Id, handed.Id, "current", null),
      ],
      await BoardAsync()
    );

    // The outgoing truck's route for its leg is passed, its leg not
    // completed: its row shows the conflict, the incoming truck's row -
    // the same load - does not.
    await SavePassedLegPlanAsync(fixture, load, outgoing);
    Assert.Equal(
      [
        (
          fixture.Truck.Id,
          outgoing.Id,
          "earlier",
          "route_passed_not_delivered"
        ),
        (incoming.Id, handed.Id, "current", null),
      ],
      await BoardAsync()
    );

    // The load's workspace places each accepted leg the same way, by its
    // own truck; the load itself stands where its active leg does.
    var workspace = (
      await DispatchWorkspaceReader.ReadAsync(
        fixture.Db,
        load.Id,
        true,
        default
      )
    )!.Response;
    await GetDispatchWorkspaceHandler.PlaceAsync(
      workspace,
      fixture.Services.PlanningInputs,
      default
    );
    Assert.Equal(
      [
        (outgoing.Id, "earlier", "route_passed_not_delivered"),
        (handed.Id, "current", null),
      ],
      workspace
        .AcceptedAssignments.DistinctBy(x => x.ExecutionLegId)
        .OrderBy(x => x.ExecutionLegId == handed.Id)
        .Select(x => (x.ExecutionLegId, x.Phase, x.Conflict))
    );
    Assert.Equal(
      ("earlier", "route_passed_not_delivered"),
      (workspace.Load.WorkPhase, workspace.Load.WorkConflict)
    );
  }

  // A saved plan for a leg whose stops are all passed, at the leg's
  // revision and inputs, committed as the tracking writer does.
  private static async Task SavePassedLegPlanAsync(
    Fixture fixture,
    Dispatch load,
    ExecutionLeg leg
  )
  {
    var inputs = (
      await fixture.Services.PlanningInputs.ReadFreshAsync(leg.TruckId, default)
    )!;
    var segment = inputs.Itinerary.Segments.Single(x =>
      x.Work.ExecutionLegId == leg.Id
    );
    var work = RouteWorkProjection.Capture(
      segment,
      inputs.Itinerary.Resources.TruckNumber
    );
    var profile = (
      await fixture.Services.Profiles.GetManyAsync([leg.TruckId], default)
    )[leg.TruckId];
    var plan = new RoutePlan
    {
      Id = Guid.NewGuid(),
      DispatchId = load.Id,
      ExecutionLegId = leg.Id,
      AssignmentRevision = leg.Revision,
      TruckId = leg.TruckId,
      Version = 1,
      CalculatedAt = DateTime.UtcNow,
      Profile = profile,
      Tracking = new() { AllStopsPassed = true },
      Route = new()
      {
        Miles = 10,
        Legs = [new(10, 600, [new(40, -80), new(41, -80)])],
      },
    };
    fixture.Db.DispatchRoutePlans.Add(
      new()
      {
        Id = plan.Id,
        DispatchId = load.Id,
        ExecutionLegId = leg.Id,
        AssignmentRevision = leg.Revision,
        TruckId = leg.TruckId,
        InputHash = RoutePlanInputs.Hash(work, profile),
        PlanJson = RoutePlanStorage.Serialize(plan),
      }
    );
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.InvalidateItem("planning-inputs", leg.TruckId);
  }

  private static async Task<(T Result, int Reads, int Captures)> Measured<T>(
    Fixture fixture,
    Func<Task<T>> read
  )
  {
    fixture.Probe.Start();
    try
    {
      var result = await read();
      return (
        result,
        fixture.Probe.Reads,
        fixture.Probe.Statements.Count(x =>
          x.Contains("'storedAssignmentRevision'")
        )
      );
    }
    finally
    {
      fixture.Probe.Stop();
    }
  }

  private static PlanningReadService Normal(Fixture fixture)
  {
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    return new(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );
  }

  private static PlanningSummaryReader Summaries(Fixture fixture) =>
    new(
      new PlanningSummaryCache(TimeProvider.System),
      fixture.Services.PlanningInputs,
      fixture.Services.Routes,
      new TestCompany(),
      fixture.Services.Reads,
      fixture.Services.Eta
    );

  // Marks the saved plan's stops passed. With invalidateInputs false the
  // row and its route cache change but the truck's cached inputs do not,
  // as on a process the invalidation has not reached yet.
  private static async Task PassAsync(
    Fixture fixture,
    DispatchRoutePlan entry,
    bool invalidateInputs
  )
  {
    var plan = RoutePlanStorage.Read(entry)!;
    plan.Tracking.AllStopsPassed = true;
    // Inside the caller's transaction the store leaves the inputs alone.
    await using (
      var transaction = await fixture.Db.Database.BeginTransactionAsync()
    )
    {
      await fixture.Services.RoutePlans.SaveAsync(entry, plan, default);
      await transaction.CommitAsync();
    }
    if (invalidateInputs)
      fixture.Services.Reads.InvalidateItem(
        "planning-inputs",
        fixture.Truck.Id
      );
  }

  // Captures of the truck's inputs a planning read made, counted by the
  // saved-plan metadata read that every capture makes exactly once.
  private static async Task<(
    AutomaticPlanningResult Result,
    int Captures
  )> CapturesOf(Fixture fixture, PlanningReadService reader)
  {
    fixture.Probe.Start();
    try
    {
      var result = await reader.ForTruckAsync(fixture.Truck.Id, default);
      return (
        result,
        fixture.Probe.Statements.Count(x =>
          x.Contains("'storedAssignmentRevision'")
        )
      );
    }
    finally
    {
      fixture.Probe.Stop();
    }
  }

  [Fact]
  public async Task ChangedStopsDoNotLetAnOldCompletedPlanSkipTheCurrentDispatch()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var saved = await fixture.SavePlanAsync(first, completed: true);
    await fixture.SavePlanAsync(next);
    first.Stops[0].Address = "Changed pickup street";
    await fixture.Db.SaveChangesAsync();
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(first.Id, result.DispatchId);
    Assert.Null(result.State);
    Assert.Empty(await fixture.Preview.GetAsync(default));
    Assert.Empty(await fixture.Db.PlanningRefreshRequests.ToListAsync());
    fixture.Probe.Stop();
    fixture.Hos.Allow = fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions { Enabled = true });
    var reader = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );
    var live = await reader.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(first.Id, live.DispatchId);
    Assert.True(live.State!.Plan!.InputsChanged);
    Assert.Single(await fixture.Db.PlanningRefreshRequests.ToListAsync());
    Assert.Equal(
      saved.PlanJson,
      await fixture
        .Db.DispatchRoutePlans.Where(x => x.Id == saved.Id)
        .Select(x => x.PlanJson)
        .SingleAsync()
    );
  }

  [Fact]
  public async Task ChangedTruckDimensionsInvalidateFleetPreviewAndRejectOldSavedGeometry()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    var fleet = Assert.Single(await fixture.Preview.GetAsync(default));
    var profile = fleet.State!.Profile;
    profile.HeightFeet = 14;
    await fixture.Services.Routes.SaveProfileAsync(load.Id, profile, default);
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(load.Id, result.DispatchId);
    Assert.Null(result.State);
    Assert.Empty(await fixture.Preview.GetAsync(default));
    Assert.Equal(0, fixture.Router.Calls);
    Assert.Equal(0, fixture.Hos.Calls);
  }

  [Fact]
  public async Task CommittedTrackingChangesInvalidateTheSavedDisplayBeforeItsTtlExpires()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    var entry = await fixture.SavePlanAsync(first);
    await fixture.SavePlanAsync(next);
    var preview = await fixture.Preview.ForTruckAsync(
      fixture.Truck.Id,
      default
    );
    Assert.Equal(first.Id, preview.DispatchId);
    var plan = preview.State!.Plan!;
    plan.Tracking.AllStopsPassed = true;
    var profiles = new TruckPlanningProfileService(
      fixture.Db,
      fixture.Services.Reads,
      fixture.Services.Settings,
      fixture.Services.ExchangeRates
    );
    await new RoutePlanStore(
      fixture.Db,
      fixture.Services.Reads,
      profiles,
      new SavedRoutePlanReader(
        fixture.Db,
        NullLogger<SavedRoutePlanReader>.Instance
      )
    ).SaveAsync(entry, plan, default);
    fixture.Probe.Start();
    Assert.Equal(
      next.Id,
      (
        await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)
      ).DispatchId
    );
  }

  [Fact]
  public async Task DispatchInvalidationChangesCurrentIdentityWithoutWaitingForBoardTtl()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = await fixture.AddAsync(1);
    var next = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(first);
    await fixture.SavePlanAsync(next);
    Assert.Equal(
      first.Id,
      (
        await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)
      ).DispatchId
    );
    first.Status = "completed";
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("dispatch");
    fixture.Services.Reads.Invalidate("board");
    fixture.Services.Reads.InvalidateItem("planning-inputs", fixture.Truck.Id);
    fixture.Probe.Start();
    Assert.Equal(
      next.Id,
      (
        await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)
      ).DispatchId
    );
  }

  [Fact]
  public async Task SplitAssignmentsRejectSavedGeometryEvenWhenItsHeaderMatches()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    var other = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "other",
      UnitNumber = "Other",
      IsActive = true,
    };
    fixture.Db.Trucks.Add(other);
    load.Stops[0].TruckId = other.Id;
    await fixture.Db.SaveChangesAsync();
    fixture.Probe.Start();
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)
    );
    Assert.Empty(await fixture.Preview.GetAsync(default));
  }

  [Fact]
  public async Task AUniqueStopOnlyAssignmentUsesTheSameServerResolutionAsPlanning()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    load.TruckId = null;
    await fixture.Db.SaveChangesAsync();
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(load.Id, result.DispatchId);
    Assert.NotNull(result.State?.Plan);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AStoredPlanCannotSupplyGeometryForAnotherTruck(
    bool changeSerializedOwner
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    var stored = await fixture.SavePlanAsync(load);
    var other = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "other",
      UnitNumber = "Other",
      IsActive = true,
    };
    fixture.Db.Trucks.Add(other);
    if (changeSerializedOwner)
    {
      var plan = JsonSerializer.Deserialize<RoutePlan>(
        stored.PlanJson,
        RoutingJson.Options
      )!;
      plan.TruckId = other.Id;
      stored.PlanJson = RoutePlanStorage.Serialize(plan);
    }
    else
      stored.TruckId = other.Id;
    await fixture.Db.SaveChangesAsync();
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(load.Id, result.DispatchId);
    Assert.Null(result.State);
  }

  [Fact]
  public async Task NoRemainingLoadHasNoCurrentIdentityAndCancellationDoesNoWork()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load, completed: true);
    fixture.Probe.Start();
    var result = await fixture.Preview.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Null(result.DispatchId);
    Assert.Null(result.State);
    fixture.Probe.Start();
    using var cancellation = new CancellationTokenSource();
    await cancellation.CancelAsync();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => fixture.Preview.ForTruckAsync(fixture.Truck.Id, cancellation.Token)
    );
    Assert.Equal(0, fixture.Probe.Reads);
    Assert.NotEmpty(new GetTruckRoutePreviewQuery(Guid.Empty).Wrong());
  }

  [Fact]
  public async Task LiveTruckReadUsesTheSnapshotWithoutARequestForBoardRows()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions());
    var reader = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );

    var result = await reader.ForTruckAsync(fixture.Truck.Id, default);

    Assert.Equal(load.Id, result.DispatchId);
    Assert.Empty(fixture.Sender.Requests);
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task FilteredBoardRowsCannotChooseTheLiveCurrentLoad()
  {
    await using var fixture = await Fixture.CreateAsync();
    var current = await fixture.AddAsync(1);
    var later = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(current);
    await fixture.SavePlanAsync(later);
    fixture.Sender.AllowTelemetry = true;
    fixture.Sender.AlterRow = row =>
      row.Dispatches.RemoveAll(x => x.Id == current.Id);
    var options = Options.Create(new SynchronizationOptions());
    var reader = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );

    var result = Assert.Single(await reader.ForBoardAsync(new(), default));
    var preview = Assert.Single(await fixture.Preview.GetAsync(default));

    Assert.Equal(current.Id, result.DispatchId);
    Assert.Equal(current.Id, preview.DispatchId);
    Assert.All(fixture.Sender.Requests, x => Assert.False(x.IncludeHos));
    Assert.Equal(0, fixture.Router.Calls);
  }

  [Fact]
  public async Task CapturedStopDetailsRefreshWithoutChangingSavedGeometry()
  {
    await using var fixture = await Fixture.CreateAsync();
    var load = await fixture.AddAsync(1);
    await fixture.SavePlanAsync(load);
    fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions());
    var reader = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );
    var first = (await reader.ForTruckAsync(fixture.Truck.Id, default))
      .State!
      .Plan!;
    load.Stops[0].Commodity = "Frozen produce";
    load.Stops[0].Notes = "Use the north gate.";
    await fixture.Db.SaveChangesAsync();
    fixture.Services.Reads.Invalidate("dispatch");
    fixture.Services.Reads.InvalidateItem("planning-inputs", fixture.Truck.Id);

    var result = (
      await reader.ForTruckAsync(
        fixture.Truck.Id,
        default,
        first.Id,
        first.Version
      )
    )
      .State!
      .Plan!;

    Assert.Equal("Frozen produce", result.Stops[0].Commodity);
    Assert.Equal("Use the north gate.", result.Stops[0].Notes);
    Assert.True(result.GeometryOmitted);
    Assert.False(result.InputsChanged);
    Assert.Empty(result.Route.Legs[0].Points);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task NativeReviewAllowsCalculationButMissingVisitsStillBlock(
    bool malformed
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var current = await fixture.AddAsync(1);
    var later = await fixture.AddAsync(2);
    await fixture.SavePlanAsync(later);
    fixture.Db.ExecutionLegs.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Trip = new() { Id = Guid.NewGuid() },
        TruckId = fixture.Truck.Id,
        Status = "active",
        Revision = 2,
        Stops = malformed ? [] : ExecutionStopRows.Capture(current.Stops),
        SourceReviewReason = malformed ? null : "Source changed.",
        Loads =
        [
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = current.Id,
            StartVisitId = current.Stops[0].Id,
            EndVisitId = current.Stops[^1].Id,
          },
        ],
      }
    );
    await fixture.Db.SaveChangesAsync();
    fixture.Sender.AllowTelemetry = true;
    var options = Options.Create(new SynchronizationOptions());
    var reader = new PlanningReadService(
      fixture.Services.Routes,
      fixture.Services.PlanningInputs,
      fixture.Services.Refreshes(fixture.Memory, options),
      fixture.Sender,
      options,
      fixture.Services.Eta,
      fixture.Services.FuelPlans
    );

    if (malformed)
    {
      await Assert.ThrowsAsync<RoutePlanningException>(
        () => fixture.Preview.ForTruckAsync(fixture.Truck.Id, default)
      );
      await Assert.ThrowsAsync<RoutePlanningException>(
        () => reader.ForTruckAsync(fixture.Truck.Id, default)
      );
      Assert.Equal(0, fixture.Router.Calls);
      return;
    }
    var pending = await reader.ForTruckAsync(fixture.Truck.Id, default);
    Assert.Equal(current.Id, pending.DispatchId);
    // The review is a notice about the load, not part of the message.
    Assert.Contains(
      pending.Notices,
      notice =>
        notice.Kind == PlanningNotice.SourceReview
        && notice.Text.StartsWith("Source changed.")
    );
    Assert.DoesNotContain("Source changed.", pending.Message ?? "");
    var leg = await fixture.Db.ExecutionLegs.SingleAsync();
    var saved = await fixture.SavePlanAsync(current);
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      saved.PlanJson,
      RoutingJson.Options
    )!;
    plan.ExecutionLegId = leg.Id;
    plan.AssignmentRevision = leg.Revision;
    saved.ExecutionLegId = leg.Id;
    saved.AssignmentRevision = leg.Revision;
    saved.InputHash = RoutePlanInputs.Hash(
      await fixture.Services.Routes.LoadAsync(current.Id, default, leg.Id),
      plan.Profile
    );
    saved.PlanJson = RoutePlanStorage.Serialize(plan);
    await fixture.Db.SaveChangesAsync();
    var live = await reader.ForTruckAsync(fixture.Truck.Id, default);
    var preview = await fixture.Preview.ForTruckAsync(
      fixture.Truck.Id,
      default
    );
    Assert.NotNull(live.State!.Plan);
    Assert.NotNull(preview.State!.Plan);
    Assert.Contains(
      live.Notices,
      notice => notice.Text.StartsWith("Source changed.")
    );
    Assert.Contains(
      preview.Notices,
      notice => notice.Text.Contains("accepted assignment")
    );
    Assert.DoesNotContain("accepted assignment", preview.Message ?? "");
    await fixture.Db.Entry(leg).ReloadAsync();
    Assert.Equal(2, leg.Revision);
    Assert.Equal("Source changed.", leg.SourceReviewReason);
    Assert.Equal("active", leg.Status);
  }

  private sealed class Fixture(
    SqliteConnection connection,
    AppDbContext db,
    ReadOnlyProbe probe
  ) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public ReadOnlyProbe Probe => probe;
    public Truck Truck { get; } =
      new()
      {
        Id = Guid.NewGuid(),
        ExternalId = "preview",
        UnitNumber = "Preview",
        IsActive = true,
      };
    public RejectingRouter Router { get; } = new();
    public RejectingHos Hos { get; } = new();
    public BoardSender Sender { get; } = new();
    public TestDriverScope Scope { get; } = new();
    public MemoryCache Memory { get; } = new(new MemoryCacheOptions());
    public ServerTelemetry Telemetry { get; } = new(new TestCompany());
    public FleetTelemetryCache TelemetryCache { get; private set; } = null!;
    public PlanningTestServices Services { get; private set; } = null!;
    public RoutePreviewService Preview { get; private set; } = null!;

    public static async Task<Fixture> CreateAsync()
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var probe = new ReadOnlyProbe();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .AddInterceptors(probe)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      var fixture = new Fixture(connection, db, probe);
      fixture.Services = new(db, fixture.Router, fixture.Sender);
      fixture.Sender.Board = new(
        db,
        fixture.Services.Reads,
        fixture.Hos,
        fixture.Services.Deadheads,
        fixture.Services.Forecasts,
        fixture.Services.Names,
        fixture.Services.Transfers,
        fixture.Scope,
        fixture.Services.PlanningInputs,
        NullLogger<GetDispatchBoardHandler>.Instance
      );
      fixture.TelemetryCache = new(fixture.Memory, new TestCompany());
      fixture.Preview = new(
        db,
        fixture.Services.PlanningInputs,
        fixture.Sender,
        fixture.Services.Reads,
        fixture.Services.Displays,
        fixture.Services.Routes,
        fixture.Memory,
        fixture.Telemetry,
        fixture.TelemetryCache,
        new TestCompany()
      );
      db.Trucks.Add(fixture.Truck);
      await db.SaveChangesAsync();
      return fixture;
    }

    public async Task<Dispatch> AddAsync(int order)
    {
      var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(order);
      var load = new Dispatch
      {
        Id = Guid.NewGuid(),
        LoadNumber = order,
        Status = order == 1 ? "in_transit" : "assigned",
        TruckId = Truck.Id,
        ShipDate = date,
        DeliveryDate = date,
        Stops =
        [
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = Truck.Id,
            Sequence = 1,
            Job = "Pick Up",
            Address = "Pickup",
            ScheduledDate = date,
            Latitude = 40,
            Longitude = -80,
          },
          new()
          {
            Id = Guid.NewGuid(),
            TruckId = Truck.Id,
            Sequence = 2,
            Job = "Drop Off",
            Address = "Delivery",
            ScheduledDate = date,
            Latitude = 41,
            Longitude = -80,
          },
        ],
      };
      Db.Dispatches.Add(load);
      await Db.SaveChangesAsync();
      return load;
    }

    public async Task<DispatchRoutePlan> SavePlanAsync(
      Dispatch load,
      bool completed = false,
      bool fuel = false
    )
    {
      var persisted = await Db
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
        Tracking = new() { AllStopsPassed = completed },
        Stops = load
          .Stops.Select(stop => new PlanStop(
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
        FuelPlan = fuel ? new() { PurchaseGallons = 100 } : null,
        FuelRecommendations = fuel ? new() : null,
      };
      var entry = new DispatchRoutePlan
      {
        Id = plan.Id,
        DispatchId = load.Id,
        TruckId = Truck.Id,
        InputHash = RoutePlanInputs.Hash(persisted, plan.Profile),
        PlanJson = RoutePlanStorage.Serialize(plan),
      };
      Db.DispatchRoutePlans.Add(entry);
      await Db.SaveChangesAsync();
      return entry;
    }

    public async ValueTask DisposeAsync()
    {
      Services.Dispose();
      TelemetryCache.Dispose();
      Memory.Dispose();
      await Db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class BoardSender : ISender
  {
    public GetDispatchBoardHandler Board { get; set; } = null!;
    public List<GetDispatchBoardQuery> Requests { get; } = [];
    public Action<TruckDispatchBoardResponse>? AlterRow { get; set; }
    public bool AllowTelemetry { get; set; }

    // Automatic planning is a writer and asks for the live position.
    public bool AllowLiveTelemetry { get; set; }
    public int TelemetryCalls { get; private set; }
    public FleetLocationsResponse Telemetry { get; set; } = new();

    public async Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken ct = default
    )
    {
      if (request is GetDispatchBoardQuery board)
      {
        Requests.Add(board);
        var response = await Board.Handle(board, ct);
        foreach (var row in response.Response?.Items ?? [])
          AlterRow?.Invoke(row);
        return (TResponse)(object)response;
      }
      if (
        AllowTelemetry && request is GetFleetLocationsQuery { CachedOnly: true }
        || AllowLiveTelemetry && request is GetFleetLocationsQuery
      )
      {
        TelemetryCalls++;
        return (TResponse)
          (object)RequestResponse<FleetLocationsResponse>.Ok(Telemetry);
      }
      throw new InvalidOperationException(
        "The preview must not request live planning data."
      );
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

  private sealed class RejectingRouter : IRoutingProvider
  {
    public bool IsConfigured => true;
    public int Calls { get; private set; }

    public Task<RoutePoint> GeocodeAsync(string address, CancellationToken ct)
    {
      Calls++;
      throw new InvalidOperationException("No provider call expected.");
    }

    public Task<TruckRoute> CalculateAsync(
      IReadOnlyList<RoutePoint> points,
      TruckRouteProfile profile,
      CancellationToken ct
    )
    {
      Calls++;
      throw new InvalidOperationException("No provider call expected.");
    }
  }

  private sealed class RejectingHos : IDriverHosProvider
  {
    public bool Allow { get; set; }
    public int Calls { get; private set; }

    public Task<IReadOnlyDictionary<string, DriverHosClocks>> GetClocksAsync(
      CancellationToken ct
    )
    {
      Calls++;
      if (!Allow)
        throw new InvalidOperationException("No HOS call expected.");
      return Task.FromResult<IReadOnlyDictionary<string, DriverHosClocks>>(
        new Dictionary<string, DriverHosClocks>()
      );
    }
  }

  private sealed class ReadOnlyProbe : DbCommandInterceptor
  {
    public bool Enabled { get; private set; }
    public int Reads { get; private set; }
    public List<string> Statements { get; } = [];

    // A writer's statements are counted too, but only reads are asserted.
    public bool AllowWrites { get; set; }

    public void Start()
    {
      Reads = 0;
      Statements.Clear();
      Enabled = true;
    }

    public void Stop() => Enabled = false;

    private void Check(DbCommand command)
    {
      if (!Enabled)
        return;
      if (!AllowWrites)
        Assert.StartsWith(
          "SELECT",
          command.CommandText.TrimStart(),
          StringComparison.OrdinalIgnoreCase
        );
      Reads++;
      Statements.Add(command.CommandText);
    }

    public override ValueTask<
      InterceptionResult<DbDataReader>
    > ReaderExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default
    )
    {
      Check(command);
      return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default
    )
    {
      Check(command);
      return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
      DbCommand command,
      CommandEventData eventData,
      InterceptionResult<object> result,
      CancellationToken cancellationToken = default
    )
    {
      Check(command);
      return ValueTask.FromResult(result);
    }
  }
}
