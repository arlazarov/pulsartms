using Application.Features.Dispatch.Commands;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Services;
using Application.Reference;
using Domain.Entities.Fleet;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Integration")]
public sealed class DispatchCreationTests
{
  [Fact]
  public async Task NativeCreationOwnsItsStopsAndRetryReturnsOriginalReceipt()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var request = Request();
    var first = await f.Creator().Handle(new(request), default);
    Assert.True(first.Success, string.Join("; ", first.Errors ?? []));
    var saved = first.Response!;
    Assert.Equal("unassigned", saved.Load.Status);
    Assert.Equal("", saved.SourceName);
    Assert.Null(saved.SourceUpdatedAt);
    Assert.True(saved.LocallyManaged);
    Assert.Equal(2, saved.Stops.Count);
    Assert.Equal("Native order", saved.Metadata.OrderNumber);
    Assert.Equal(1200m, saved.Load.Price);
    Assert.False(await f.Db.DispatchSourceLinks.AnyAsync());
    var workspace = await f.Db.DispatchWorkspaces.SingleAsync();
    Assert.True(workspace.OwnsStops && workspace.OwnsCommercial);
    var retry = await f.Creator().Handle(new(request), default);
    Assert.True(retry.Success, string.Join("; ", retry.Errors ?? []));
    Assert.Equal(saved.Load.Id, retry.Response!.Load.Id);
    Assert.Equal(2, await f.Db.Dispatches.CountAsync());
    Assert.Single(await f.Db.DispatchWorkspaceRevisions.ToListAsync());
    request.OrderNumber = "Different command";
    Assert.Equal(
      409,
      (await f.Creator().Handle(new(request), default)).StatusCode
    );
  }

  [Fact]
  public async Task NativeLoadCanBeAssignedAndCompletedWithoutImport()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var created = (await f.Creator().Handle(new(Request()), default)).Response!;
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "fixture",
      UnitNumber = "native",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    var first = created.Load.Stops[0];
    var assigned = await f.AssignmentHandler()
      .Handle(
        new(
          created.Load.Id,
          new(truck.UnitNumber, first.Id, 0, first.CompletionIdentity)
        ),
        default
      );
    Assert.True(assigned.Success, string.Join("; ", assigned.Errors ?? []));
    var leg = await f.Db.ExecutionLegs.SingleAsync();
    Assert.Equal("planned", leg.Status);
    Assert.Equal(truck.Id, leg.TruckId);
    var load = await f
      .Db.Dispatches.Include(x => x.Stops)
      .SingleAsync(x => x.Id == created.Load.Id);
    Assert.Equal("assigned", load.Status);
    var current = await DispatchWorkspaceReader.ReadAsync(
      f.Db,
      load.Id,
      true,
      default
    );
    foreach (var stop in current!.Response.Load.Stops)
    {
      var completed = await f.Handler()
        .Handle(
          new(
            load.Id,
            stop.Id,
            new(f.Clock.GetUtcNow().AddMinutes(-1), 0, stop.CompletionIdentity)
          ),
          default
        );
      Assert.True(completed.Success, string.Join("; ", completed.Errors ?? []));
    }
    var work = await ExecutionWorkReader.ReadAsync(
      f.Db,
      DateOnly.FromDateTime(f.Clock.GetUtcNow().UtcDateTime),
      new FleetNames(f.Db),
      new ActiveTransfers(f.Db),
      null,
      true,
      true,
      default
    );
    Assert.DoesNotContain(work.SelectMany(x => x.Loads), x => x.Id == load.Id);
    Assert.Equal("completed", leg.Status);
    Assert.Null(leg.StartedAt);
    Assert.Equal(
      f.Clock.GetUtcNow().AddMinutes(-1).UtcDateTime,
      leg.CompletedAt
    );
    Assert.Equal(3, await f.Db.ExecutionLegRevisions.CountAsync());
    Assert.Equal(3, await f.Db.ExecutionPlanningChanges.CountAsync());
  }

  [Theory]
  [InlineData("Viewer")]
  [InlineData(null)]
  public async Task OnlyOperatorsCanCreate(string? role)
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    Assert.Equal(
      403,
      (await f.Creator(role).Handle(new(Request()), default)).StatusCode
    );
    Assert.Single(await f.Db.Dispatches.ToListAsync());
  }

  [Theory]
  [InlineData("missing-key")]
  [InlineData("one-stop")]
  [InlineData("delivery-first")]
  [InlineData("missing-location")]
  [InlineData("duplicate-stop")]
  [InlineData("assignment")]
  public async Task InvalidCreationLeavesNoReceiptOrNumberReservation(
    string kind
  )
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var request = Request();
    switch (kind)
    {
      case "missing-key":
        request.IdempotencyKey = Guid.Empty;
        break;
      case "one-stop":
        request.Stops.RemoveAt(1);
        break;
      case "delivery-first":
        request.Stops[0].Job = "Delivery";
        break;
      case "missing-location":
        request.Stops[0].Latitude = null;
        break;
      case "duplicate-stop":
        request.Stops[1].Id = request.Stops[0].Id;
        break;
      case "assignment":
        request.Stops[0].TruckId = Guid.NewGuid();
        break;
    }
    Assert.Equal(
      400,
      (await f.Creator().Handle(new(request), default)).StatusCode
    );
    Assert.False(await f.Db.DispatchWorkspaceRevisions.AnyAsync());
    Assert.False(await f.Db.DispatchNumberCounters.AnyAsync());
  }

  [Fact]
  public async Task CreationCannotStealExistingStopIdentity()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var request = Request();
    request.Stops[0].Id = f.Load.Stops[0].Id;
    Assert.Equal(
      409,
      (await f.Creator().Handle(new(request), default)).StatusCode
    );
    Assert.False(await f.Db.DispatchNumberCounters.AnyAsync());
  }

  [Fact]
  public async Task NativeLoadCanBeEditedWithoutImport()
  {
    await using var f = await StopCompletionFixture.CreateAsync();
    var created = (await f.Creator().Handle(new(Request()), default)).Response!;
    var update = new UpdateDispatchWorkspaceRequest
    {
      IdempotencyKey = Guid.NewGuid(),
      ExpectedRevision = created.Revision,
      SourceFingerprint = created.SourceFingerprint,
      Metadata = created.Metadata,
      Stops = created.Stops,
    };
    update.Metadata.OrderNumber = "Edited locally";
    update.Stops[1].Name = "Final customer";
    var saved = await f.WorkspaceEditor()
      .Handle(new(created.Load.Id, update), default);
    Assert.True(saved.Success, string.Join("; ", saved.Errors ?? []));
    Assert.Equal("Edited locally", saved.Response!.Metadata.OrderNumber);
    Assert.Equal("Final customer", saved.Response.Stops[1].Name);
  }

  internal static CreateDispatchRequest Request() =>
    new()
    {
      IdempotencyKey = Guid.NewGuid(),
      OrderNumber = "Native order",
      CustomerName = "Native customer",
      Currency = "USD",
      Price = 1200m,
      Stops = [Stop("Pick Up", 1), Stop("Delivery", 2)],
    };

  private static DispatchWorkspaceStop Stop(string job, int sequence) =>
    new()
    {
      Id = Guid.NewGuid(),
      Sequence = sequence,
      Job = job,
      Name = "Facility",
      Address = "100 Main Street",
      City = "Toronto",
      Province = "ON",
      Country = "CA",
      Latitude = 43.65m + sequence,
      Longitude = -79.38m,
      AppointmentMode = "unscheduled",
    };
}
