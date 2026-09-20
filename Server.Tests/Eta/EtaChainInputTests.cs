using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Queries;
using Application.Features.Eta.Models;
using Application.Features.Eta.Services;
using Application.Features.Execution.Models;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Integration")]
public sealed partial class EtaChainInputTests
{
  [Fact]
  public async Task CompetingStartedLoadsBlockCurrentEta()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Next.Status = "in_transit";
    await fixture.Db.SaveChangesAsync();

    var description = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    var plan = await fixture.Services.EtaInputs.PrepareAsync(
      description!,
      default
    );

    Assert.Equal(
      WorkSequenceProblem.CompetingCurrentWork,
      Assert.Single(description!.Sequence.Issues).Problem
    );
    Assert.Contains("multiple loads", plan.CurrentUnavailableReason);
  }

  [Fact]
  public async Task FutureAmbiguityPreservesCurrentEtaAndInvalidatesInputs()
  {
    await using var fixture = await Fixture.CreateAsync();
    var later = new Load
    {
      Id = Guid.NewGuid(),
      TruckId = fixture.Truck.Id,
      LoadNumber = 102,
      Status = "assigned",
      ShipDate = fixture.Next.ShipDate!.Value.AddDays(1),
      Stops =
      [
        new()
        {
          Id = Guid.NewGuid(),
          Sequence = 1,
          Job = "Pick Up",
          TruckId = fixture.Truck.Id,
        },
      ],
    };
    fixture.Db.Dispatches.Add(later);
    await fixture.Db.SaveChangesAsync();
    var before = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    Assert.Empty(before!.Sequence.Issues);
    later.ShipDate = null;
    await fixture.Db.SaveChangesAsync();

    var after = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    var plan = await fixture.Services.EtaInputs.PrepareAsync(after!, default);

    Assert.NotEqual(before.InputHash, after!.InputHash);
    Assert.Equal(fixture.Current.Id, after.RootDispatchId);
    Assert.Null(plan.CurrentUnavailableReason);
    Assert.Equal(
      fixture.Next.Id,
      Assert.Single(after.Sequence.Issues).Work.DispatchId
    );
    Assert.Contains("order", plan.Future[0].UnavailableReason);
    Assert.Equal(0, fixture.Services.Hos.ClockCalls);
  }

  [Fact]
  public async Task EqualUpcomingAppointmentsDoNotUseLoadNumberAsPrecedence()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Current.Status = "assigned";
    fixture.Next.Stops[0].ScheduledTime = fixture
      .Current
      .Stops[0]
      .ScheduledTime;
    await fixture.Db.SaveChangesAsync();

    var description = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    var plan = await fixture.Services.EtaInputs.PrepareAsync(
      description!,
      default
    );

    Assert.Equal(
      WorkSequenceProblem.UnknownOrder,
      Assert.Single(description!.Sequence.Issues).Problem
    );
    Assert.Contains("order", plan.CurrentUnavailableReason);
  }

  [Fact]
  public async Task DirectSelectionMatchesSuppliedInputsWithoutBoardRequests()
  {
    var sender = new DispatchTelemetrySender(new());
    await using var fixture = await Fixture.CreateAsync(sender: sender);
    var expected = Assert
      .Single(
        await fixture.Services.EtaInputs.DescribeManyAsync(
          [
            new()
            {
              TruckId = fixture.Truck.Id,
              Dispatches = fixture.Ordered.Reverse().ToList(),
            },
          ],
          default
        )
      )
      .Value;

    var actual = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );

    Assert.NotNull(expected);
    Assert.NotNull(actual);
    Assert.Equal(expected.InputHash, actual.InputHash);
    Assert.Equal(expected.GeometryHash, actual.GeometryHash);
    Assert.Equal(
      new[] { fixture.Current.Id, fixture.Next.Id },
      actual.Loads.Select(x => x.Id)
    );
    Assert.Equal(0, sender.Calls);
    Assert.Equal(0, fixture.Services.Hos.ClockCalls);
    Assert.Equal(0, fixture.Services.Hos.HistoryCalls);
    Assert.Equal(0, fixture.Probe.GeometryReads);
    Assert.False(fixture.Db.ChangeTracker.HasChanges());
  }

  [Fact]
  public async Task DirectNativeSelectionUsesTheLegDriverRatherThanTruckDriver()
  {
    var sender = new DispatchTelemetrySender(new());
    await using var fixture = await Fixture.CreateAsync(sender: sender);
    var previous = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "previous",
      Name = "Previous",
    };
    var receiving = new Driver
    {
      Id = Guid.NewGuid(),
      ExternalId = "receiving",
      Name = "Receiving",
    };
    fixture.Db.Drivers.AddRange(previous, receiving);
    fixture.Truck.DriverId = previous.Id;
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new Trip { Id = Guid.NewGuid() },
      TruckId = fixture.Truck.Id,
      DriverId = receiving.Id,
      Status = "active",
      Revision = 3,
      Stops = ExecutionStopRows.Capture(fixture.Current.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = fixture.Current.Id,
          StartVisitId = fixture.Current.Stops[0].Id,
          EndVisitId = fixture.Current.Stops[^1].Id,
        },
      ],
    };
    fixture.Db.ExecutionLegs.Add(leg);
    await fixture.Db.SaveChangesAsync();
    var board = await fixture.Services.Board.Handle(
      new(
        TruckId: fixture.Truck.Id,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    var expected = Assert
      .Single(
        await fixture.Services.EtaInputs.DescribeManyAsync(
          board.Response!.Items,
          default
        )
      )
      .Value;

    var actual = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );

    Assert.NotNull(actual);
    Assert.Equal(leg.Id, actual.RootExecutionLegId);
    Assert.Equal(receiving.Id, actual.DriverId);
    Assert.Equal("receiving", actual.DriverExternalId);
    Assert.Equal(expected!.InputHash, actual.InputHash);
    Assert.Equal(expected.GeometryHash, actual.GeometryHash);
    // The accepted load in hand and the one assigned after it: the forecast
    // follows the truck's own work rather than stopping at the first leg.
    Assert.Equal(2, actual.Loads.Length);
    Assert.Equal(leg.Id, actual.Loads[0].ExecutionLegId);
    Assert.Null(actual.Loads[1].ExecutionLegId);
    Assert.Equal(0, sender.Calls);
    Assert.Equal(0, fixture.Services.Hos.ClockCalls);
  }

  [Fact]
  public async Task DirectSelectionHonorsCancellationBeforeReadingInputs()
  {
    await using var fixture = await Fixture.CreateAsync();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () =>
        fixture.Services.EtaInputs.DescribeAsync(
          fixture.Truck.Id,
          cancellation.Token
        )
    );

    Assert.Equal(0, fixture.Probe.MetadataReads);
    Assert.Equal(0, fixture.Probe.GeometryReads);
  }

  // Work accepted into execution ahead of the load in hand is still this
  // truck's work, and the forecast follows it. The chain used to end at the
  // first leg of any kind - a rule written when loads were accepted one at a
  // time; accepted days ahead, as they are now, it left every truck without
  // an arrival beyond the load it was driving.
  [Theory]
  [InlineData("planned")]
  [InlineData("active")]
  public async Task AcceptedWorkAheadJoinsTheChain(string nativeStatus)
  {
    await using var fixture = await Fixture.CreateAsync();
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new Trip { Id = Guid.NewGuid() },
      TruckId = fixture.Truck.Id,
      Status = nativeStatus,
      Revision = 1,
      Stops = ExecutionStopRows.Capture(fixture.Next.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = fixture.Next.Id,
          StartVisitId = fixture.Next.Stops[0].Id,
          EndVisitId = fixture.Next.Stops[^1].Id,
        },
      ],
    };
    fixture.Db.ExecutionLegs.Add(leg);
    await fixture.Db.SaveChangesAsync();

    var description = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );

    Assert.NotNull(description);
    var nativeRoot = nativeStatus == "active";
    Assert.Equal(
      nativeRoot ? fixture.Next.Id : fixture.Current.Id,
      description.RootDispatchId
    );
    Assert.Equal(2, description.Itinerary.Segments.Length);
    Assert.Equal(2, description.Loads.Length);
    Assert.Empty(description.Exclusions);

    var prepared = await fixture.Services.EtaInputs.PrepareAsync(
      description,
      default
    );

    // The load ahead is carried, with a reason where a road of its own or
    // the connection to it has not been saved yet - a sentence, not a blank.
    var ahead = Assert.Single(prepared.Future);
    Assert.Equal(
      nativeRoot ? fixture.Current.Id : fixture.Next.Id,
      ahead.DispatchId
    );
  }

  [Fact]
  public async Task BatchDescriptionsKeepInputHashesAndReadRootMetadataOnlyOnce()
  {
    await using var fixture = await Fixture.CreateAsync();
    var single = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    var reads = fixture.Probe.MetadataReads;
    var batch = await fixture.Services.EtaInputs.DescribeManyAsync(
      [
        new()
        {
          TruckId = fixture.Truck.Id,
          Dispatches = fixture.Ordered.ToList(),
        },
        new() { TruckId = Guid.NewGuid(), Dispatches = [] },
      ],
      default
    );
    var result = Assert.Single(batch).Value;
    Assert.Equal(single.InputHash, result.InputHash);
    Assert.Equal(single.GeometryHash, result.GeometryHash);
    Assert.Equal(
      single.Loads.Select(x => x.Id),
      result.Loads.Select(x => x.Id)
    );
    Assert.Equal(reads + 1, fixture.Probe.MetadataReads);
    Assert.Empty(
      await new SavedRoutePlanReader(fixture.Db).ReadManyAsync(
        [Guid.NewGuid()],
        default
      )
    );
    Assert.Empty(fixture.Probe.TruckAssignmentQueries);
  }

  [Fact]
  public async Task FuelRecommendationRefreshDoesNotInvalidateTheDailyAllowanceForecast()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    var saved = await fixture.Db.DispatchRoutePlans.SingleAsync();
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      saved.PlanJson,
      RoutePlanningService.Json
    )!;
    plan.FuelPlan = new() { CalculatedAt = DateTime.UtcNow };
    saved.PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json);
    await fixture.Db.SaveChangesAsync();
    var updated = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    Assert.Equal(first.InputHash, updated.InputHash);
    Assert.Equal(first.GeometryHash, updated.GeometryHash);
  }

  [Fact]
  public async Task ColdDescriptionReadsCompactMetadataWithoutTransferringDenseGeometry()
  {
    await using var fixture = await Fixture.CreateAsync(denseRoot: true);
    var description = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    Assert.Equal(fixture.Current.Id, description!.RootDispatchId);
    Assert.Equal(1, fixture.Probe.MetadataReads);
    Assert.Equal(0, fixture.Probe.GeometryReads);
    var metadata = (
      await new SavedRoutePlanReader(fixture.Db).ReadAsync(
        fixture.Current.Id,
        default
      )
    )!;
    Assert.Equal(fixture.Truck.Id, metadata.TruckId);
    Assert.Equal(fixture.Truck.Id, metadata.PlanTruckId);
    Assert.Equal(1, metadata.Version);
    Assert.True(JsonSerializer.SerializeToUtf8Bytes(metadata).Length < 2048);
    Assert.Equal(0, fixture.Probe.GeometryReads);
  }

  [Fact]
  public void PostgreSqlMetadataQueryProjectsOnlyCompactJson()
  {
    using var db = new AppDbContext(
      new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=unused;Database=translation_only")
        .Options
    );
    var query = (IQueryable)
      typeof(SavedRoutePlanReader)
        .GetMethod("Metadata", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(new SavedRoutePlanReader(db), [Guid.NewGuid()])!;
    var sql = query.ToQueryString();
    Assert.Contains("jsonb_build_object", sql);
    Assert.Contains("AS \"Value\"", sql);
    Assert.Contains("{fuelPlan,calculatedAt}", sql);
    Assert.DoesNotContain("AS \"PlanJson\"", sql);
    Assert.DoesNotContain("RouteJson", sql);
    Assert.Contains("AS MATERIALIZED", sql);
    Assert.Contains("ANY", sql);
    Assert.Equal(1, sql.Split("\"PlanJson\"::jsonb").Length - 1);
  }

  [Fact]
  public async Task CompactMetadataPreservesTrackingFuelAndRejectsWrongPlanOwnership()
  {
    await using var fixture = await Fixture.CreateAsync();
    var saved = await fixture.Db.DispatchRoutePlans.SingleAsync();
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      saved.PlanJson,
      RoutePlanningService.Json
    )!;
    var now = DateTime.UtcNow;
    plan.Tracking = new()
    {
      AllStopsPassed = true,
      PassedStopIds = [fixture.Current.Stops[0].Id],
      VisitedStops = new() { [fixture.Current.Stops[0].Id] = now },
      NextStopId = fixture.Current.Stops[^1].Id,
      NextStopLabel = "Delivery",
      OffRouteSince = now,
    };
    plan.FuelPlan = new() { CalculatedAt = now };
    saved.PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json);
    await fixture.Db.SaveChangesAsync();
    var metadata = (
      await new SavedRoutePlanReader(fixture.Db).ReadAsync(
        fixture.Current.Id,
        default
      )
    )!;
    Assert.Equal(plan.Id, metadata.PlanId);
    Assert.Equal(now, metadata.FuelCalculatedAt);
    Assert.Equal(now, metadata.Tracking.OffRouteSince);
    Assert.Equal(
      now,
      metadata.Tracking.VisitedStops[fixture.Current.Stops[0].Id]
    );
    Assert.Equal(plan.Tracking.PassedStopIds, metadata.Tracking.PassedStopIds);
    Assert.Equal(plan.Tracking.NextStopId, metadata.Tracking.NextStopId);
    Assert.Equal("Delivery", metadata.Tracking.NextStopLabel);
    var completed = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    Assert.Equal(fixture.Next.Id, completed!.RootDispatchId);
    plan.TruckId = Guid.NewGuid();
    saved.PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json);
    await fixture.Db.SaveChangesAsync();
    var wrongOwner = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    Assert.Equal(fixture.Current.Id, wrongOwner!.RootDispatchId);
  }

  [Fact]
  public async Task WarmAndScheduleOnlyReadsReuseSavedGeometryButRefreshAppointments()
  {
    await using var fixture = await Fixture.CreateAsync();
    var first = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    var original = await fixture.Services.EtaInputs.PrepareAsync(
      first,
      default
    );
    Assert.Null(Assert.Single(original.Future).UnavailableReason);
    var geometryReads = fixture.Probe.GeometryReads;
    var warm = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    await fixture.Services.EtaInputs.PrepareAsync(warm, default);
    Assert.Equal(geometryReads, fixture.Probe.GeometryReads);
    fixture.Next.Stops[0].ScheduledTime = new(14, 30);
    await fixture.Db.SaveChangesAsync();
    var changed = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    var prepared = await fixture.Services.EtaInputs.PrepareAsync(
      changed,
      default
    );
    Assert.NotEqual(first.InputHash, changed.InputHash);
    Assert.Equal(first.GeometryHash, changed.GeometryHash);
    Assert.Equal(geometryReads, fixture.Probe.GeometryReads);
    Assert.Equal(
      new TimeOnly(14, 30),
      prepared.Future[0].Stops[0].ScheduledTime
    );
    Assert.Equal(fixture.Next.Stops[0].Id, prepared.Future[0].Stops[0].Id);
  }

  [Fact]
  public async Task OverlappingAppointmentsReuseTheSavedConnectionWithoutZeroingItsTravelTime()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Next.Stops[0].ScheduledTime = new(9, 0);
    await fixture.Db.SaveChangesAsync();

    var description = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    var prepared = await fixture.Services.EtaInputs.PrepareAsync(
      description,
      default
    );
    var future = Assert.Single(prepared.Future);

    Assert.Null(future.UnavailableReason);
    Assert.NotNull(future.Connection);
    Assert.True(future.Connection.HasCompleteTravelTimes);
    Assert.Equal(new TimeOnly(9, 0), future.Stops[0].ScheduledTime);
    Assert.Equal(
      fixture.Current.Id,
      description.Connections[fixture.Next.Id]!.Previous.Id
    );
  }

  [Fact]
  public async Task WrongSavedPredecessorCannotBecomeAZeroTimeConnection()
  {
    await using var fixture = await Fixture.CreateAsync();
    var saved = await fixture.Db.DispatchDeadheads.SingleAsync();
    saved.PreviousDispatchId = fixture.Next.Id;
    await fixture.Db.SaveChangesAsync();
    var description = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    var chain = await fixture.Services.EtaInputs.PrepareAsync(
      description,
      default
    );
    Assert.NotNull(Assert.Single(chain.Future).UnavailableReason);
    Assert.Null(chain.Future[0].Connection);
  }

  [Fact]
  public async Task ReceivingDriverAssignmentBlocksOldClocksWithoutRemovingTheTruckRoute()
  {
    await using var fixture = await Fixture.CreateAsync();
    var description = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    description = description with
    {
      Loads = description.Loads.SetItem(
        0,
        description.Loads[0] with
        {
          DriverId = Guid.NewGuid(),
        }
      ),
    };
    var chain = await fixture.Services.EtaInputs.PrepareAsync(
      description,
      default
    );
    Assert.Contains("receiving driver", chain.CurrentUnavailableReason);
    Assert.NotEmpty(chain.CurrentActivities);
    Assert.NotNull(chain.Future[0].Route);
  }

  [Fact]
  public async Task BoardReadInvalidatesTheRootWhenAFutureAppointmentChanges()
  {
    await using var fixture = await Fixture.CreateAsync();
    var description = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    var now = DateTime.UtcNow;
    var rootEta = new DispatchEta(
      now,
      now.AddMinutes(2),
      [
        new(fixture.Current.Stops[^1].Id, now, "Etc/UTC", null, 0, 60, 0)
        {
          DispatchId = fixture.Current.Id,
        },
      ],
      null,
      []
    );
    var nextEta = rootEta with
    {
      Stops =
      [
        new(
          fixture.Next.Stops[0].Id,
          now.AddHours(2),
          "Etc/UTC",
          null,
          0,
          120,
          0
        )
        {
          DispatchId = fixture.Next.Id,
        },
      ],
    };
    Assert.True(
      await new EtaForecastStore(fixture.Db).SaveAsync(
        [
          new(
            fixture.Current.Id,
            fixture.Truck.Id,
            fixture.Current.Id,
            description.InputHash,
            description.DriverExternalId,
            rootEta
          ),
          new(
            fixture.Next.Id,
            fixture.Truck.Id,
            fixture.Current.Id,
            description.InputHash,
            description.DriverExternalId,
            nextEta
          ),
        ],
        default
      )
    );
    fixture.Services.EtaMemory.Demand(
      fixture.Current.Id,
      description.InputHash,
      now
    );
    fixture.Services.EtaMemory.Results[fixture.Current.Id] = new(
      "cached",
      rootEta
    )
    {
      ChainInputHash = description.InputHash,
    };
    var initial = await fixture.Services.Board.Handle(
      new(
        TruckId: fixture.Truck.Id,
        IncludeHos: false,
        IncludeFinancials: false
      ),
      default
    );
    Assert.All(
      Assert.Single(initial.Response!.Items).Dispatches,
      load => Assert.NotEmpty(load.Eta!.Stops)
    );
    fixture.Next.Stops[0].ScheduledTime = new(14, 30);
    await fixture.Db.SaveChangesAsync();
    var changed = await fixture.Services.Board.Handle(
      new(
        TruckId: fixture.Truck.Id,
        IncludeHos: false,
        IncludeFinancials: false
      ),
      default
    );
    Assert.All(
      Assert.Single(changed.Response!.Items).Dispatches,
      load => Assert.Empty(load.Eta!.Stops)
    );
    Assert.False(
      fixture.Services.EtaMemory.Results.ContainsKey(fixture.Current.Id)
    );
    Assert.Equal([fixture.Current.Id], fixture.Services.EtaMemory.Viewed.Keys);
  }

  [Fact]
  public async Task ScreenRowsCannotOmitOrInventAssignments()
  {
    await using var fixture = await Fixture.CreateAsync();
    var direct = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    var batch = await fixture.Services.EtaInputs.DescribeManyAsync(
      [
        new()
        {
          TruckId = fixture.Truck.Id,
          Dispatches =
          [
            new()
            {
              Id = Guid.NewGuid(),
              ExecutionLegId = Guid.NewGuid(),
              ExecutionStatus = "active",
              TruckId = fixture.Truck.Id,
            },
          ],
        },
      ],
      default
    );
    var result = Assert.Single(batch).Value;
    Assert.Equal(direct!.InputHash, result.InputHash);
    Assert.Equal(
      new[] { fixture.Current.Id, fixture.Next.Id },
      result.Loads.Select(x => x.Id)
    );
  }

  [Fact]
  public async Task ExcludedUpcomingWorkRemainsInTheInputSignature()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Next.Status = "planned";
    await fixture.Db.SaveChangesAsync();
    var before = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    Assert.Equal(2, before.Itinerary.Segments.Length);
    Assert.Single(before.Loads);
    Assert.Equal(
      EtaWorkExclusionReason.LegacyNotAssigned,
      Assert.Single(before.Exclusions).Reason
    );
    fixture.Next.Stops[0].ScheduledTime = new(15, 0);
    await fixture.Db.SaveChangesAsync();
    var after = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    Assert.NotEqual(before.InputHash, after.InputHash);
    Assert.Equal(before.GeometryHash, after.GeometryHash);
  }

  [Fact]
  public async Task OverdueUpcomingWorkIsExcludedWithoutHidingIt()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Next.DeliveryDate = DateOnly
      .FromDateTime(DateTime.UtcNow)
      .AddDays(-1);
    await fixture.Db.SaveChangesAsync();
    var description = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    Assert.Equal(2, description.Itinerary.Segments.Length);
    Assert.Single(description.Loads);
    Assert.Equal(
      EtaWorkExclusionReason.OverdueUpcoming,
      Assert.Single(description.Exclusions).Reason
    );
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task AddressEvidenceAndRouteIdentitySurviveTheSnapshot(
    bool verified
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    var stop = fixture.Next.Stops[0];
    stop.Address = "123 Main Road";
    stop.City = "Charlotte";
    stop.SourceAddressJson = StopAddress.From(stop).Serialize();
    stop.AddressVerifiedAt = verified ? DateTime.UtcNow : null;
    stop.AddressRetryAfter = verified ? null : DateTime.UtcNow.AddHours(1);
    stop.ManualAction = "Pick Up";
    stop.ManualStateAfter = "Loaded";
    stop.CompletionOverride = false;
    stop.ManualCompletionRevision = 3;
    fixture.Next.RouteChoiceRevision = 7;
    await fixture.Db.SaveChangesAsync();
    var original = await fixture.Services.Routes.LoadAsync(
      fixture.Next.Id,
      default
    );
    var description = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    var projected = description.Loads.Single(x => x.Id == fixture.Next.Id);
    Assert.Equal(
      RoutePlanningService.HashInputs(original, description.Profile),
      RoutePlanningService.HashInputs(projected, description.Profile)
    );
    Assert.Equal(
      BaseRouteService.Signature(original, description.Profile),
      BaseRouteService.Signature(projected, description.Profile)
    );
    Assert.Equal(
      StopLocation.ReliablePoint(stop, DateTime.UtcNow),
      StopLocation.ReliablePoint(projected.Stops[0], DateTime.UtcNow)
    );
    Assert.NotNull(
      StopLocation.ReliablePoint(projected.Stops[0], DateTime.UtcNow)
    );
    Assert.Equal("Pick Up", projected.Stops[0].ManualAction);
    Assert.Equal(3, projected.Stops[0].ManualCompletionRevision);
    var previous = await fixture
      .Db.Dispatches.AsNoTracking()
      .Include(x => x.Stops)
      .SingleAsync(x => x.Id == fixture.Current.Id);
    var expected = DeadheadConnection.Find(
      original,
      [RouteWorkProjection.Capture(previous), original]
    );
    Assert.Equal(
      expected!.Signature(description.Profile),
      description.Connections[fixture.Next.Id]!.Signature(description.Profile)
    );
  }

  [Fact]
  public async Task CachedAssignmentsCannotOverrideFreshWork()
  {
    await using var fixture = await Fixture.CreateAsync();
    await fixture.Services.Routes.LoadAsync(fixture.Current.Id, default);
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new Trip { Id = Guid.NewGuid() },
      TruckId = fixture.Truck.Id,
      Status = "active",
      Revision = 4,
      Stops = ExecutionStopRows.Capture(fixture.Current.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = fixture.Current.Id,
          StartVisitId = fixture.Current.Stops[0].Id,
          EndVisitId = fixture.Current.Stops[^1].Id,
        },
      ],
    };
    fixture.Db.ExecutionLegs.Add(leg);
    await fixture.Db.SaveChangesAsync();
    var description = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    Assert.Equal(leg.Id, description.RootExecutionLegId);
    Assert.Equal(4, description.Loads[0].AssignmentRevision);
  }

  [Fact]
  public async Task DescriptionCannotRevalidateInsideAnOlderTransaction()
  {
    await using var fixture = await Fixture.CreateAsync();
    await using var transaction =
      await fixture.Db.Database.BeginTransactionAsync();
    await Assert.ThrowsAsync<InvalidOperationException>(
      () => fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    );
    Assert.Same(transaction, fixture.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task AManualStartRetainsTheExactTruckPathAndRouteSignature()
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Current.Stops.Insert(
      0,
      new()
      {
        Id = Guid.NewGuid(),
        Sequence = 0,
        ManualAction = "Driver start",
        ManualStateAfter = "No truck",
      }
    );
    fixture.Db.DispatchStops.Add(fixture.Current.Stops[0]);
    fixture.Current.PlanningFromStopId = fixture.Current.Stops[1].Id;
    fixture.Current.PlanningTruckId = fixture.Truck.Id;
    fixture.Current.PlanningAssignmentRevision = 3;
    await fixture.Db.SaveChangesAsync();
    var original = await fixture.Services.Routes.LoadAsync(
      fixture.Current.Id,
      default
    );
    var description = (
      await fixture.Services.EtaInputs.DescribeAsync(fixture.Truck.Id, default)
    )!;
    Assert.Equal(3, description.Itinerary.Segments[0].Visits.Length);
    Assert.False(description.Itinerary.Segments[0].Visits[0].InTruckPath);
    Assert.Equal(
      original.Stops.Select(x => x.Id),
      description.Loads[0].Stops.Select(x => x.Id)
    );
    Assert.Equal(
      RoutePlanningService.HashInputs(original, description.Profile),
      RoutePlanningService.HashInputs(description.Loads[0], description.Profile)
    );
  }

  [Theory]
  [InlineData(true, "active")]
  [InlineData(false, "active")]
  [InlineData(false, "planned")]
  public async Task NativeReviewUsesAcceptedWorkAndMissingVisitsBlock(
    bool malformed,
    string status
  )
  {
    await using var fixture = await Fixture.CreateAsync();
    fixture.Db.ExecutionLegs.Add(
      new()
      {
        Id = Guid.NewGuid(),
        Trip = new Trip { Id = Guid.NewGuid() },
        TruckId = fixture.Truck.Id,
        Status = status,
        Revision = 2,
        Stops = malformed
          ? []
          : ExecutionStopRows.Capture(fixture.Current.Stops),
        SourceReviewReason = malformed ? null : "Source changed.",
        Loads =
        [
          new()
          {
            Id = Guid.NewGuid(),
            DispatchId = fixture.Current.Id,
            StartVisitId = fixture.Current.Stops[0].Id,
            EndVisitId = fixture.Current.Stops[^1].Id,
          },
        ],
      }
    );
    await fixture.Db.SaveChangesAsync();
    var description = await fixture.Services.EtaInputs.DescribeAsync(
      fixture.Truck.Id,
      default
    );
    if (malformed)
      Assert.Null(description);
    else
    {
      Assert.NotNull(description);
      Assert.Equal(fixture.Current.Id, description.RootDispatchId);
      Assert.NotNull(description.RootExecutionLegId);
      Assert.Contains(
        WorkReadProblem.SourceReviewRequired,
        description.Itinerary.Segments[0].Problems
      );
    }
  }

  private sealed class Fixture(
    SqliteConnection connection,
    AppDbContext db,
    GeometryProbe probe,
    PlanningTestServices services,
    Truck truck,
    Load current,
    Load next,
    PublicationProbe publication
  ) : IAsyncDisposable
  {
    public AppDbContext Db => db;
    public PublicationProbe Publication => publication;
    public GeometryProbe Probe => probe;
    public PlanningTestServices Services => services;
    public Truck Truck => truck;
    public Load Current => current;
    public Load Next => next;
    public IReadOnlyList<DispatchResponse> Ordered =>
      [
        new() { Id = current.Id, TruckId = truck.Id },
        new() { Id = next.Id, TruckId = truck.Id },
      ];

    public static async Task<Fixture> CreateAsync(
      bool denseRoot = false,
      ISender? sender = null
    )
    {
      var connection = new SqliteConnection("Data Source=:memory:");
      await connection.OpenAsync();
      var probe = new GeometryProbe();
      var db = new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>()
          .UseSqlite(connection)
          .AddInterceptors(probe)
          .Options
      );
      await db.Database.EnsureCreatedAsync();
      var truck = new Truck
      {
        Id = Guid.NewGuid(),
        ExternalId = "eta-chain",
        UnitNumber = "ETA",
        IsActive = true,
      };
      var day = DateOnly.FromDateTime(DateTime.UtcNow);
      Load Make(
        int number,
        string status,
        int pickup,
        int delivery,
        decimal longitude
      ) =>
        new()
        {
          Id = Guid.NewGuid(),
          LoadNumber = number,
          Status = status,
          TruckId = truck.Id,
          ShipDate = day,
          DeliveryDate = day,
          Stops =
          [
            new()
            {
              Id = Guid.NewGuid(),
              Sequence = 1,
              Job = "Pick Up",
              TruckId = truck.Id,
              ScheduledDate = day,
              ScheduledTime = new(pickup, 0),
              Latitude = 35,
              Longitude = longitude,
            },
            new()
            {
              Id = Guid.NewGuid(),
              Sequence = 2,
              Job = "Drop Off",
              TruckId = truck.Id,
              ScheduledDate = day,
              ScheduledTime = new(delivery, 0),
              Latitude = 35,
              Longitude = longitude + 1,
            },
          ],
        };
      var current = Make(100, "in_transit", 8, 10, -81);
      var next = Make(101, "assigned", 14, 16, -79);
      db.Trucks.Add(truck);
      db.Dispatches.AddRange(current, next);
      await db.SaveChangesAsync();
      var publication = new PublicationProbe(db);
      var services = new PlanningTestServices(
        db,
        sender: sender,
        publicationScope: publication
      );
      var profile = await services.Routes.ProfileAsync(truck.Id, default);
      var persisted = await db
        .Dispatches.AsNoTracking()
        .Include(x => x.Stops)
        .ToDictionaryAsync(x => x.Id);
      static TruckRoute Road(double from, double to) =>
        new()
        {
          Miles = 60,
          Seconds = 3600,
          Legs = [new(60, 3600, [new(35, from), new(35, to)])],
        };
      var plan = new RoutePlan
      {
        Id = Guid.NewGuid(),
        Version = 1,
        DispatchId = current.Id,
        TruckId = truck.Id,
        Profile = profile,
        FromCurrentPosition = true,
        Route = Road(-81, -80),
        Stops =
        [
          new(current.Stops[^1].Id, "Delivery", "", 2, new(35, -80))
          {
            Job = "Drop Off",
          },
        ],
      };
      if (denseRoot)
        plan.Route.Legs =
        [
          new(
            60,
            3600,
            Enumerable
              .Range(0, 10_001)
              .Select(i => new RoutePoint(35, -81 + i / 10_000d))
              .ToList()
          ),
        ];
      db.DispatchRoutePlans.Add(
        new()
        {
          Id = plan.Id,
          DispatchId = current.Id,
          TruckId = truck.Id,
          InputHash = RoutePlanningService.HashInputs(
            persisted[current.Id],
            profile
          ),
          PlanJson = JsonSerializer.Serialize(plan, RoutePlanningService.Json),
        }
      );
      db.DispatchBaseRoutes.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = next.Id,
          CalculatedAt = DateTime.UtcNow,
          InputHash = BaseRouteService.Signature(persisted[next.Id], profile),
          RouteJson = JsonSerializer.Serialize(
            Road(-79, -78),
            RoutePlanningService.Json
          ),
        }
      );
      var pair = DeadheadConnection.Find(
        persisted[next.Id],
        [persisted[current.Id]]
      )!;
      db.DispatchDeadheads.Add(
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = next.Id,
          PreviousDispatchId = current.Id,
          InputHash = pair.Signature(profile),
          Miles = 60,
          CalculatedAt = DateTime.UtcNow,
          RouteJson = JsonSerializer.Serialize(
            Road(-80, -79),
            RoutePlanningService.Json
          ),
        }
      );
      await db.SaveChangesAsync();
      return new(
        connection,
        db,
        probe,
        services,
        truck,
        current,
        next,
        publication
      );
    }

    public async ValueTask DisposeAsync()
    {
      services.Dispose();
      await db.DisposeAsync();
      await connection.DisposeAsync();
    }
  }

  private sealed class GeometryProbe : DbCommandInterceptor
  {
    public int GeometryReads { get; private set; }
    public int MetadataReads { get; private set; }
    public List<string> TruckAssignmentQueries { get; } = [];

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
      DbCommand command,
      CommandExecutedEventData eventData,
      DbDataReader result,
      CancellationToken cancellationToken = default
    )
    {
      var columns = Enumerable
        .Range(0, result.FieldCount)
        .Select(result.GetName)
        .ToArray();
      if (
        columns.Contains("UnitNumber")
        && columns.Contains("ExternalId")
        && columns.Contains("DriverId")
      )
        TruckAssignmentQueries.Add(command.CommandText);
      if (columns.Any(x => x is "RouteJson" or "PlanJson"))
        GeometryReads++;
      if (
        columns.Contains("Value")
        && command.CommandText.Contains("json_object", StringComparison.Ordinal)
      )
        MetadataReads++;
      return ValueTask.FromResult(result);
    }
  }
}
