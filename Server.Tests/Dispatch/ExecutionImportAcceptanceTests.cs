using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Reference;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class ExecutionImportAcceptanceTests
{
  [Fact]
  public async Task RawResourceProposalSurvivesLocalOwnershipAndImportReplay()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    await SyncAsync(f);
    var load = await f.Db.Dispatches.Include(x => x.Stops).SingleAsync();
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    var acceptedRevision = leg.Revision;
    f.Db.DispatchWorkspaces.Add(
      new()
      {
        Id = load.Id,
        OwnsStops = true,
        SourceStopsJson = ExecutionSnapshots.Write(load.Stops),
      }
    );
    load.Stops[0].TruckNumber = "Locally retained label";
    await f.Db.SaveChangesAsync();
    source.TruckNumber = "Unknown header truck";
    source.Stops[0].TruckNumber = "Unknown pickup truck";
    source.Stops[1].TruckNumber = "Unknown delivery truck";
    for (var attempt = 0; attempt < 2; attempt++)
    {
      f.Memory.Compact(1);
      f.Db.ChangeTracker.Clear();
      await SyncAsync(f);
      var state = await DispatchWorkspaceReader.ReadAsync(
        f.Db,
        load.Id,
        true,
        default
      );
      var proposal = Assert.IsType<DispatchAssignmentProposal>(
        state!.Response.SourceAssignment
      );
      Assert.Equal(source.TruckNumber, proposal.Header.Truck);
      Assert.Equal(
        source.Stops.Select(x => x.TruckNumber),
        proposal.Visits.Select(x => x.Resources.Truck)
      );
      Assert.All(proposal.Visits, x => Assert.Null(x.Resources.TruckId));
      Assert.Equal(new[] { 1, 2 }, proposal.Visits.Select(x => x.Sequence));
      var accepted = Assert.Single(state.Response.AcceptedAssignments);
      Assert.Equal(leg.Id, accepted.ExecutionLegId);
      Assert.Equal(acceptedRevision, accepted.Revision);
      Assert.Equal("source", accepted.Resources.Truck);
      Assert.Equal(
        "Locally retained label",
        state.Load.Stops.Single(x => x.Id == load.Stops[0].Id).TruckNumber
      );
      Assert.Equal(1, await f.Db.ExecutionLegRevisions.CountAsync());
      Assert.NotNull(state.Response.SourceReviewReason);
    }
  }

  [Theory]
  [InlineData("planned")]
  [InlineData("active")]
  [InlineData("completed")]
  public async Task SourceCreatesAcceptedExecutionWithoutInventedActorOrStart(
    string status
  )
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    if (status != "planned")
      source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-2);
    if (status == "completed")
    {
      source.Status = "completed";
      source.Stops[1].DeliveredAt = DateTime.UtcNow.AddHours(-1);
    }
    await SyncAsync(f);
    var leg = await f
      .Db.ExecutionLegs.Include(x => x.Loads)
      .Include(x => x.Trip)
      .SingleAsync();
    Assert.Equal(status, leg.Status);
    Assert.Null(leg.RecordedBy);
    Assert.Null(leg.Trip.RecordedBy);
    Assert.Null(leg.StartedAt);
    Assert.Equal(source.Stops[^1].DeliveredAt, leg.CompletedAt);
    var revision = await f.Db.ExecutionLegRevisions.SingleAsync();
    Assert.Equal("source-assignment-accepted", revision.Operation);
    Assert.Null(revision.RecordedBy);
    var provenance = await f.Db.DispatchSourceLinks.SingleAsync();
    Assert.Equal(provenance.AssignmentSignature, leg.SourceAssignmentSignature);
    Assert.Equal(
      leg.SourceAssignmentSignature,
      ExecutionRevisionFacts.Read(revision).SourceAssignmentSignature
    );
    Assert.Null(provenance.ExecutionReviewReason);
    Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
    Assert.Equal(2, leg.Stops.Count);
    Assert.All(
      leg.Stops,
      x => Assert.Equal(provenance.DispatchId, x.DispatchId)
    );
    f.Memory.Compact(1);
    f.Db.ChangeTracker.Clear();
    await SyncAsync(f);
    Assert.Single(await f.Db.Trips.ToListAsync());
    Assert.Single(await f.Db.ExecutionLegRevisions.ToListAsync());
    Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
  }

  [Theory]
  [InlineData("mixed-truck")]
  [InlineData("unknown-truck")]
  [InlineData("inactive-truck")]
  [InlineData("header-conflict")]
  [InlineData("future-actual")]
  [InlineData("status-without-facts")]
  [InlineData("driver-travel")]
  [InlineData("unknown-driver")]
  [InlineData("duplicate-sequence")]
  [InlineData("reversed-visits")]
  [InlineData("completed-with-invalid-actuals")]
  public async Task AmbiguousSourceIsVisibleButCannotBecomePlanningAuthority(
    string kind
  )
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    var truck = await f.Db.Trucks.SingleAsync();
    switch (kind)
    {
      case "mixed-truck":
        f.Db.Trucks.Add(
          new()
          {
            Id = Guid.NewGuid(),
            UnitNumber = "other",
            ExternalId = "other",
            IsActive = true,
          }
        );
        source.Stops[1].TruckNumber = "other";
        break;
      case "unknown-truck":
        source.Stops[1].TruckNumber = "missing";
        break;
      case "inactive-truck":
        truck.IsActive = false;
        break;
      case "header-conflict":
        source.DriverName = "Unresolved header driver";
        break;
      case "future-actual":
        source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(1);
        break;
      case "status-without-facts":
        source.Status = "in_transit";
        break;
      case "driver-travel":
        source.Stops[0].Job = "Driver start";
        break;
      case "unknown-driver":
        source.Stops[1].DriverName = "Unknown";
        break;
      case "duplicate-sequence":
        source.Stops[1].Sequence = source.Stops[0].Sequence;
        break;
      case "reversed-visits":
      case "completed-with-invalid-actuals":
        if (kind == "completed-with-invalid-actuals")
          source.Status = "completed";
        source.Stops[0].PickedUpAt = DateTime.UtcNow.AddMinutes(-10);
        source.Stops[1].DeliveredAt = DateTime.UtcNow.AddHours(-1);
        break;
    }
    await f.Db.SaveChangesAsync();
    await SyncAsync(f);
    Assert.False(await f.Db.ExecutionLegs.AnyAsync());
    Assert.False(await f.Db.ExecutionLegRevisions.AnyAsync());
    Assert.False(await f.Db.ExecutionPlanningChanges.AnyAsync());
    var sourceLink = await f.Db.DispatchSourceLinks.SingleAsync();
    Assert.NotNull(sourceLink.ExecutionReviewReason);
    var workspace = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      sourceLink.DispatchId,
      true,
      default
    );
    Assert.Equal(2, workspace!.Response.Stops.Count);
    Assert.NotNull(workspace.Response.SourceReviewReason);
    if (kind != "inactive-truck")
    {
      var itinerary = await new TruckItineraryReader(
        f.Db,
        new ExecutionReadScope(f.Db),
        new FleetNames(f.Db),
        new ActiveTransfers(f.Db)
      ).ReadAsync(truck.Id, DateTimeOffset.UtcNow, default);
      Assert.NotNull(itinerary);
      Assert.Contains(
        itinerary.Segments,
        x => x.Problems.Contains(WorkReadProblem.SourceReviewRequired)
      );
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ResolvedSourceResourcesReplaceAcceptedWork(bool complete)
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-2);
    await SyncAsync(f);
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    var originalTruck = leg.TruckId;
    var replacement = new Truck
    {
      Id = Guid.NewGuid(),
      UnitNumber = "replacement",
      ExternalId = "replacement",
      IsActive = true,
    };
    f.Db.Trucks.Add(replacement);
    await f.Db.SaveChangesAsync();
    source.TruckNumber = "replacement";
    foreach (var stop in source.Stops)
      stop.TruckNumber = "replacement";
    if (complete)
      source.Stops[1].DeliveredAt = DateTime.UtcNow.AddMinutes(-10);
    await SyncAsync(f);
    Assert.Equal(replacement.Id, leg.TruckId);
    Assert.Equal(
      (await f.Db.DispatchSourceLinks.SingleAsync()).AssignmentSignature,
      leg.SourceAssignmentSignature
    );
    Assert.Equal(complete ? "completed" : "active", leg.Status);
    Assert.Equal(
      source.Stops[1].DeliveredAt,
      leg.Stops.OrderBy(x => x.Position).Last().DeliveredAt
    );
    Assert.Null(leg.SourceReviewReason);
    Assert.Equal(2, leg.Revision);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Contains(
      await f.Db.ExecutionPlanningChanges.ToListAsync(),
      x => x.TruckId == originalTruck
    );
    Assert.Contains(
      await f.Db.ExecutionPlanningChanges.ToListAsync(),
      x => x.TruckId == replacement.Id && x.AssignmentRevision == 2
    );
    var requests = await f.Db.ExecutionPlanningChanges.CountAsync();
    f.Memory.Compact(1);
    await SyncAsync(f);
    Assert.Equal(2, leg.Revision);
    Assert.Equal(2, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(requests, await f.Db.ExecutionPlanningChanges.CountAsync());
  }

  [Fact]
  public async Task SourceTrailerChangeOverridesLocalWorkspaceAndIsIdempotent()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    var trailer = new Trailer
    {
      Id = Guid.NewGuid(),
      UnitNumber = "44120",
      ExternalId = "44120",
      IsActive = true,
    };
    f.Db.Trailers.Add(trailer);
    await f.Db.SaveChangesAsync();
    await SyncAsync(f);
    var load = await f.Db.Dispatches.Include(x => x.Stops).SingleAsync();
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    f.Db.DispatchWorkspaces.Add(
      new()
      {
        Id = load.Id,
        OwnsStops = true,
        SourceStopsJson = ExecutionSnapshots.Write(load.Stops),
      }
    );
    await f.Db.SaveChangesAsync();
    source.TrailerNumber = "44120";
    foreach (var stop in source.Stops)
      stop.TrailerNumber = "44120";
    await SyncAsync(f);
    Assert.Equal(trailer.Id, leg.TrailerId);
    Assert.All(leg.Stops, x => Assert.Equal("44120", x.TrailerNumber));
    Assert.Null(leg.SourceReviewReason);
    Assert.Equal(2, leg.Revision);
    f.Memory.Compact(1);
    f.Db.ChangeTracker.Clear();
    await SyncAsync(f);
    var replay = await f.Db.ExecutionLegs.SingleAsync();
    Assert.Equal(trailer.Id, replay.TrailerId);
    Assert.Equal(2, replay.Revision);
    Assert.Null(replay.SourceReviewReason);
  }

  [Fact]
  public async Task ContradictorySourceResourcesRemainVisibleWithoutGuessing()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    await SyncAsync(f);
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    var truck = leg.TruckId;
    source.TruckNumber = "unknown";
    await SyncAsync(f);
    Assert.Equal(truck, leg.TruckId);
    Assert.Equal(1, leg.Revision);
    Assert.Contains("unresolved", leg.SourceReviewReason);
  }

  [Fact]
  public async Task LaterImportCannotReverseAcceptedVisitChronology()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddMinutes(-10);
    await SyncAsync(f);
    source.Stops[1].DeliveredAt = DateTime.UtcNow.AddHours(-1);
    await SyncAsync(f);
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    Assert.Equal("active", leg.Status);
    Assert.Null(leg.CompletedAt);
    Assert.Contains("visit order", leg.SourceReviewReason);
    Assert.Null(leg.Stops.OrderBy(x => x.Position).Last().DeliveredAt);
  }

  [Fact]
  public async Task PlannedImportStartsAndCompletesThroughAcceptedFacts()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    await SyncAsync(f);
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    Assert.Equal("planned", leg.Status);
    source.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-2);
    await SyncAsync(f);
    Assert.Equal("active", leg.Status);
    Assert.Null(leg.StartedAt);
    source.Status = "completed";
    source.Stops[1].DeliveredAt = DateTime.UtcNow.AddHours(-1);
    await SyncAsync(f);
    Assert.Equal("completed", leg.Status);
    Assert.Equal(source.Stops[1].DeliveredAt, leg.CompletedAt);
    Assert.Equal(3, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(3, await f.Db.ExecutionPlanningChanges.CountAsync());
    f.Memory.Compact(1);
    await SyncAsync(f);
    Assert.Equal(3, await f.Db.ExecutionLegRevisions.CountAsync());
  }

  [Fact]
  public async Task PlannedImportsCannotClaimTheSameActiveResource()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var first = await SeedAsync(f);
    var second = Source();
    second.ExternalId = "second";
    second.LoadNumber = 2;
    f.Sources.Add(second);
    await SyncAsync(f);
    first.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-1);
    second.Stops[0].PickedUpAt = first.Stops[0].PickedUpAt;
    await SyncAsync(f);
    var legs = await f.Db.ExecutionLegs.ToListAsync();
    Assert.Equal(2, legs.Count);
    Assert.All(
      legs,
      x =>
      {
        Assert.Equal("planned", x.Status);
        Assert.Contains("conflicts", x.SourceReviewReason);
        Assert.Equal(
          first.Stops[0].PickedUpAt,
          x.Stops.OrderBy(s => s.Position).First().PickedUpAt
        );
      }
    );
    var count = await f.Db.ExecutionLegRevisions.CountAsync();
    f.Memory.Compact(1);
    await SyncAsync(f);
    Assert.Equal(count, await f.Db.ExecutionLegRevisions.CountAsync());
  }

  [Fact]
  public async Task FinalDeliveryDoesNotCloseWorkWithAnUnfinishedPickup()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var source = await SeedAsync(f);
    await SyncAsync(f);
    source.Stops[1].DeliveredAt = DateTime.UtcNow.AddHours(-1);
    await SyncAsync(f);
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    Assert.Equal("active", leg.Status);
    Assert.Null(leg.CompletedAt);
  }

  [Fact]
  public async Task CompetingSourceLoadsRequireReviewRegardlessOfBatchOrder()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var first = await SeedAsync(f);
    first.Stops[0].PickedUpAt = DateTime.UtcNow.AddHours(-1);
    var second = Source();
    second.ExternalId = "second";
    second.LoadNumber = 2;
    second.Stops[0].PickedUpAt = first.Stops[0].PickedUpAt;
    f.Sources.Add(second);
    await SyncAsync(f);
    Assert.False(await f.Db.ExecutionLegs.AnyAsync());
    Assert.All(
      await f.Db.DispatchSourceLinks.ToListAsync(),
      x => Assert.Contains("conflicts", x.ExecutionReviewReason)
    );
    f.Sources.Reverse();
    f.Memory.Compact(1);
    await SyncAsync(f);
    Assert.False(await f.Db.ExecutionLegs.AnyAsync());
  }

  [Fact]
  public async Task MissingCatalogResourceCanBeResolvedOnLaterImport()
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    f.Sources.Add(Source());
    await SyncAsync(f);
    Assert.False(await f.Db.ExecutionLegs.AnyAsync());
    f.Db.Trucks.Add(
      new()
      {
        Id = Guid.NewGuid(),
        UnitNumber = "source",
        ExternalId = "source",
        IsActive = true,
      }
    );
    await f.Db.SaveChangesAsync();
    f.Reads.Invalidate("fleet-catalog");
    await SyncAsync(f);
    Assert.Single(await f.Db.ExecutionLegs.ToListAsync());
    Assert.Null(
      (await f.Db.DispatchSourceLinks.SingleAsync()).ExecutionReviewReason
    );
  }

  private static async Task<ExternalDispatch> SeedAsync(DispatchSyncFixture f)
  {
    f.Db.Trucks.Add(
      new()
      {
        Id = Guid.NewGuid(),
        UnitNumber = "source",
        ExternalId = "source",
        IsActive = true,
      }
    );
    await f.Db.SaveChangesAsync();
    var source = Source();
    f.Sources.Add(source);
    return source;
  }

  private static ExternalDispatch Source() =>
    new()
    {
      ExternalId = "source-load",
      LoadNumber = 1,
      Status = "assigned",
      TruckNumber = "source",
      Stops = new[] { "Pick Up", "Delivery" }
        .Select(
          (job, i) =>
            new ExternalDispatchStop
            {
              Sequence = i + 1,
              Job = job,
              Name = $"Visit {i}",
              Address = $"{i + 1} Main Road",
              City = "Toronto",
              Province = "ON",
              Country = "CA",
              TruckNumber = "source",
            }
        )
        .ToList(),
    };

  private static async Task SyncAsync(DispatchSyncFixture f)
  {
    var result = await f.Handler.Handle(new(), default);
    Assert.True(result.Success, string.Join(";", result.Errors ?? []));
  }
}
