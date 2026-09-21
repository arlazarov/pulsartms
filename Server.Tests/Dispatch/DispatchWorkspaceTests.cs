using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Interfaces;
using Application.Interfaces;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Domain.Models.Routing;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchWorkspaceTests
{
  [Fact]
  public async Task StartingPickupAllowsDetailsButRetainsItsPosition()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    f.Load.PlanningFromStopId = f.Load.Stops[0].Id;
    await f.Db.SaveChangesAsync();
    var request = await Draft(f);
    var first = request.Stops[0];
    Assert.True(first.CanEdit);
    Assert.False(first.CanMove);
    Assert.False(first.CanRemove);
    first.Name = "Corrected pickup";
    first.Address = "Updated pickup address";
    var saved = await Handler(f).Handle(new(f.Load.Id, request), default);
    Assert.True(saved.Success, string.Join(" ", saved.Errors ?? []));
    Assert.Equal("Updated pickup address", saved.Response!.Stops[0].Address);
    Assert.Equal(first.Id, f.Load.PlanningFromStopId);
    request = await Draft(f);
    request.Stops.RemoveAt(0);
    Assert.Equal(
      400,
      (await Handler(f).Handle(new(f.Load.Id, request), default)).StatusCode
    );
  }

  [Fact]
  public async Task DriverAdjustmentsAreAuditedWithoutChangingRateOrInvoice()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    f.Load.Price = 1000m;
    f.Load.Currency = "USD";
    var driver = new Driver { Id = Guid.NewGuid(), Name = "Known driver" };
    f.Db.Drivers.Add(driver);
    await f.Db.SaveChangesAsync();
    var draft = await Draft(f);
    var adjustmentId = Guid.NewGuid();
    draft.Metadata.Adjustments.Add(
      new()
      {
        Id = adjustmentId,
        Target = "driver",
        Direction = "deduction",
        DriverId = driver.Id,
        DriverName = "Untrusted label",
        Currency = "USD",
        Amount = 30m,
        Reason = "Recorded charge",
      }
    );
    var saved = await Handler(f).Handle(new(f.Load.Id, draft), default);
    Assert.True(saved.Success);
    var row = Assert.Single(saved.Response!.Metadata.Adjustments);
    Assert.Equal(adjustmentId, row.Id);
    Assert.Equal(driver.Id, row.DriverId);
    Assert.Equal(driver.Name, row.DriverName);
    Assert.Equal(1000m, saved.Response.BillingTotals!.InvoiceAmount);
    Assert.Equal(1000m, f.Load.Price);
    var retry = await Handler(f).Handle(new(f.Load.Id, draft), default);
    Assert.True(retry.Success);
    Assert.Equal(1, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  [Fact]
  public async Task TwoEditorsCannotOverwriteTheSameOpenedRevision()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    f.Db.Users.Add(
      new()
      {
        Id = Guid.NewGuid(),
        IdentityUserId = "second-editor",
        Name = "Second editor",
        Email = "second@example.invalid",
      }
    );
    await f.Db.SaveChangesAsync();
    var first = await Draft(f);
    var second = await Draft(f);
    first.Metadata.BrokerContact = "First editor contact";
    second.Metadata.BrokerContact = "Second editor contact";
    second.Stops[1].Notes = "Stale instructions";
    Assert.True(
      (await Handler(f).Handle(new(f.Load.Id, first), default)).Success
    );
    var other = new UpdateDispatchWorkspaceHandler(
      f.Db,
      new Caller("second-editor"),
      new Roles("Dispatch"),
      f.Clock,
      f.Reads,
      f.Queue
    );
    var conflict = await other.Handle(new(f.Load.Id, second), default);
    Assert.Equal(409, conflict.StatusCode);
    var saved = await Draft(f);
    Assert.Equal("First editor contact", saved.Metadata.BrokerContact);
    Assert.NotEqual("Stale instructions", saved.Stops[1].Notes);
    Assert.Equal(1, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  [Fact]
  public async Task ProviderImportInvalidatesOpenedEditorBeforeSave()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    await f.Db.SaveChangesAsync();
    var draft = await Draft(f);
    draft.Metadata.BrokerContact = "Stale editor";
    var stop = f.Load.Stops[1];
    var source = Source(stop);
    source.Notes = "Updated by provider";
    DispatchMapper.UpdateStop(stop, source, null, null, null, null);
    await f.Db.SaveChangesAsync();
    var result = await Handler(f).Handle(new(f.Load.Id, draft), default);
    Assert.Equal(409, result.StatusCode);
    Assert.Equal("Updated by provider", stop.Notes);
    Assert.Equal(0, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  [Fact]
  public async Task NativeAssignmentRevisionInvalidatesOpenedEditor()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    var truck = new Truck { Id = Guid.NewGuid(), UnitNumber = "fixture" };
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      Status = "active",
      Revision = 1,
      Stops = ExecutionStopRows.Capture(f.Load.Stops),
    };
    f.Db.Trucks.Add(truck);
    f.Db.Trips.Add(trip);
    f.Db.ExecutionLegs.Add(leg);
    f.Db.LoadExecutionLegs.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = f.Load.Id,
        ExecutionLegId = leg.Id,
        Sequence = 1,
      }
    );
    await f.Db.SaveChangesAsync();
    var draft = await Draft(f);
    leg.Revision++;
    await f.Db.SaveChangesAsync();
    draft.Metadata.BrokerContact = "Stale editor";
    var result = await Handler(f).Handle(new(f.Load.Id, draft), default);
    Assert.Equal(409, result.StatusCode);
    Assert.Equal(2, leg.Revision);
    Assert.Equal(0, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  [Fact]
  public async Task SaveReordersStopsAndPreservesSourceOwnershipAndRetry()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    await f.Db.SaveChangesAsync();
    var source = f.Load.Stops.Select(Source).ToArray();
    var request = await Draft(f);
    var first = request.Stops[2];
    request.Stops.RemoveAt(2);
    request.Stops.Insert(1, first);
    request.Metadata.BrokerContact = "Fixture contact";
    request.Metadata.Price = 1200m;
    request.Metadata.Currency = "USD";
    var command = new UpdateDispatchWorkspaceCommand(f.Load.Id, request);
    var saved = await Handler(f).Handle(command, default);
    Assert.True(saved.Success, string.Join(" ", saved.Errors ?? []));
    Assert.Equal(
      request.Stops.Select(x => x.Id),
      saved.Response!.Stops.Select(x => x.Id)
    );
    Assert.Equal(1200m, f.Load.Price);
    Assert.True(saved.Response.LocallyManaged);
    var workspace = await f.Db.DispatchWorkspaces.SingleAsync();
    DispatchWorkspaceImport.MergeStops(
      f.Load,
      workspace,
      source,
      f.Clock.GetUtcNow().UtcDateTime
    );
    Assert.Equal(
      request.Stops.Select(x => x.Id),
      f.Load.Stops.OrderBy(x => x.Sequence).Select(x => x.Id)
    );
    var retry = await Handler(f).Handle(command, default);
    Assert.True(retry.Success, string.Join(" ", retry.Errors ?? []));
    Assert.Equal(1, await f.Db.DispatchWorkspaceRevisions.CountAsync());
    f.Db.Users.Add(
      new()
      {
        Id = Guid.NewGuid(),
        IdentityUserId = "other",
        Name = "Other dispatcher",
        Email = "other@example.invalid",
      }
    );
    await f.Db.SaveChangesAsync();
    var other = new UpdateDispatchWorkspaceHandler(
      f.Db,
      new Caller("other"),
      new Roles("Dispatch"),
      f.Clock,
      f.Reads,
      f.Queue
    );
    Assert.Equal(409, (await other.Handle(command, default)).StatusCode);
    request.Metadata.BrokerContact = "Changed payload";
    Assert.Equal(409, (await Handler(f).Handle(command, default)).StatusCode);
  }

  [Fact]
  public async Task FutureNativeReorderUpdatesSnapshotAndDurablePlanning()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    var truck = new Truck { Id = Guid.NewGuid(), UnitNumber = "fixture" };
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      Status = "active",
      Revision = 1,
      Stops = ExecutionStopRows.Capture(f.Load.Stops),
    };
    var link = new LoadExecutionLeg
    {
      Id = Guid.NewGuid(),
      DispatchId = f.Load.Id,
      ExecutionLegId = leg.Id,
      Sequence = 1,
    };
    f.Db.Trucks.Add(truck);
    f.Db.Trips.Add(trip);
    f.Db.ExecutionLegs.Add(leg);
    f.Db.LoadExecutionLegs.Add(link);
    await f.Db.SaveChangesAsync();
    var request = await Draft(f);
    (request.Stops[1], request.Stops[2]) = (request.Stops[2], request.Stops[1]);
    var result = await Handler(f).Handle(new(f.Load.Id, request), default);
    Assert.True(result.Success, string.Join(" ", result.Errors ?? []));
    Assert.Equal(2, leg.Revision);
    Assert.Equal(
      request.Stops.Select(x => x.Id),
      ExecutionStopRows.Read(leg).Select(x => x.Id)
    );
    Assert.Contains(
      await f.Db.ExecutionPlanningChanges.ToListAsync(),
      x => x.ExecutionLegId == leg.Id && x.AssignmentRevision == 2
    );
    var version = await f.Db.ExecutionLegRevisions.SingleAsync();
    Assert.Equal(request.IdempotencyKey, version.CorrelationId);
    Assert.Equal(f.Actor.Id, version.RecordedBy);
    Assert.Equal(2, version.Revision);
    Assert.Equal(
      request.Stops.Select(x => x.Id),
      ExecutionRevisionFacts.Read(version).Stops.Select(x => x.Id)
    );
  }

  [Fact]
  public async Task RecordedStopDetailsCanBeCorrectedButStaleDraftIsRejected()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    f.Load.Stops[0].PickedUpAt = DateTime.UtcNow.AddDays(-1);
    await f.Db.SaveChangesAsync();
    var request = await Draft(f);
    Assert.True(request.Stops[0].CanEdit);
    Assert.False(request.Stops[0].CanMove);
    Assert.False(request.Stops[0].CanRemove);
    var actual = f.Load.Stops[0].PickedUpAt;
    request.Stops[0].Notes = "Corrected instructions";
    var saved = await Handler(f).Handle(new(f.Load.Id, request), default);
    Assert.True(saved.Success, string.Join(" ", saved.Errors ?? []));
    Assert.Equal(actual, f.Load.Stops[0].PickedUpAt);
    Assert.Equal("Corrected instructions", saved.Response!.Stops[0].Notes);
    request = await Draft(f);
    f.Load.Stops[2].ScheduledDate = new(2026, 10, 1);
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      409,
      (await Handler(f).Handle(new(f.Load.Id, request), default)).StatusCode
    );
    Assert.Equal(1, await f.Db.DispatchWorkspaceRevisions.CountAsync());
  }

  [Fact]
  public async Task NewStopAndRemovalPersistWithoutImporterResurrection()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    await f.Db.SaveChangesAsync();
    var imported = f.Load.Stops.Select(Source).ToArray();
    var request = await Draft(f);
    var removed = request.Stops[2].Id;
    request.Stops.RemoveAt(2);
    var added = DispatchWorkspaceData.Read<DispatchWorkspaceStop>(
      DispatchWorkspaceData.Write(request.Stops[1])
    );
    added.Id = Guid.NewGuid();
    added.IsNew = true;
    added.Name = "New delivery";
    request.Stops.Insert(2, added);
    var result = await Handler(f).Handle(new(f.Load.Id, request), default);
    Assert.True(result.Success, string.Join(" ", result.Errors ?? []));
    DispatchWorkspaceImport.MergeStops(
      f.Load,
      await f.Db.DispatchWorkspaces.SingleAsync(),
      imported,
      DateTime.UtcNow
    );
    Assert.DoesNotContain(f.Load.Stops, x => x.Id == removed);
    Assert.Contains(f.Load.Stops, x => x.Id == added.Id);
  }

  [Fact]
  public async Task ActorMustBeActiveAndAuthorized()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Prepare(f);
    var request = await Draft(f);
    Assert.Equal(
      403,
      (
        await Handler(f, "Driver").Handle(new(f.Load.Id, request), default)
      ).StatusCode
    );
    f.Actor.IsActive = false;
    await f.Db.SaveChangesAsync();
    Assert.Equal(
      403,
      (await Handler(f).Handle(new(f.Load.Id, request), default)).StatusCode
    );
  }

  [Fact]
  public async Task ExplicitAddressVerificationDoesNotWriteOrOpenTransaction()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var geocoder = new Geocoder(f);
    var handler = new VerifyDispatchAddressHandler(
      f.Db,
      new Caller(),
      new Roles("Dispatch"),
      geocoder
    );
    var result = await handler.Handle(
      new(
        f.Load.Id,
        new()
        {
          Address = "1 Main St",
          City = "Fixture",
          Country = "US",
        }
      ),
      default
    );
    Assert.True(result.Success);
    Assert.Equal(40m, result.Response!.Latitude);
    Assert.Equal(1, geocoder.Calls);
    Assert.Equal(0, await f.Db.DispatchWorkspaces.CountAsync());
  }

  private sealed class Geocoder(StopCompletionFixture fixture)
    : IAddressGeocoder
  {
    public int Calls { get; private set; }

    public Task<RoutePoint> GeocodeAsync(
      string address,
      CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<ResolvedAddress> ResolveAsync(
      string address,
      CancellationToken ct
    )
    {
      Assert.Null(fixture.Db.Database.CurrentTransaction);
      Calls++;
      return Task.FromResult(
        new ResolvedAddress(
          new(40, -80),
          "1 Main Street",
          "Fixture",
          "PA",
          "US",
          "15000"
        )
      );
    }
  }

  private static void Prepare(StopCompletionFixture f)
  {
    foreach (var stop in f.Load.Stops)
    {
      stop.Job = stop.Sequence == 1 ? "Pick Up" : "Delivery";
      stop.Latitude = 35m + stop.Sequence;
      stop.Longitude = -80m;
    }
  }

  private static async Task<UpdateDispatchWorkspaceRequest> Draft(
    StopCompletionFixture f
  )
  {
    var state = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      f.Load.Id,
      true,
      default
    );
    return new()
    {
      ExpectedRevision = state!.Response.Revision,
      SourceFingerprint = state.Response.SourceFingerprint,
      IdempotencyKey = Guid.NewGuid(),
      Metadata = state.Response.Metadata,
      Stops = state.Response.Stops,
    };
  }

  private static UpdateDispatchWorkspaceHandler Handler(
    StopCompletionFixture f,
    string role = "Dispatch"
  ) => new(f.Db, new Caller(), new Roles(role), f.Clock, f.Reads, f.Queue);

  private sealed class Caller(string identity = "operator") : ICurrentUser
  {
    public bool IsAuthenticated => true;
    public string IdentityUserId => identity;
  }

  private sealed class Roles(string role) : IUserRoleService
  {
    public Task<string?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult<string?>(role);

    public Task<Dictionary<Guid, string>> GetAsync(
      IReadOnlyCollection<Guid> ids,
      CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task SetAsync(
      string id,
      string value,
      CancellationToken ct = default
    ) => throw new NotSupportedException();
  }

  private static ExternalDispatchStop Source(DispatchStop stop) =>
    new()
    {
      Sequence = stop.Sequence,
      Job = stop.Job,
      Name = stop.Name,
      Address = stop.Address,
      City = stop.City,
      Province = stop.Province,
      Country = stop.Country,
      ZipCode = stop.ZipCode,
      Latitude = stop.Latitude,
      Longitude = stop.Longitude,
    };
}
