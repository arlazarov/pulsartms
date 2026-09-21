using Application.Features.Dispatch.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Domain.Models.Execution;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Dispatch;

[Trait("Category", "Synchronization")]
[Trait("Kind", "Integration")]
public sealed class DispatchAppointmentSyncTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ProviderScheduleChangeReachesNativePlanningDespiteLocalNotes(
    bool localAppointment
  )
  {
    await using var f = await DispatchSyncFixture.CreateAsync();
    var truck = new Truck
    {
      Id = Guid.NewGuid(),
      ExternalId = "appointment-fixture",
      UnitNumber = "11005",
      IsActive = true,
    };
    f.Db.Trucks.Add(truck);
    await f.Db.SaveChangesAsync();
    var stop = new ExternalDispatchStop
    {
      Sequence = 1,
      Job = "Drop Off",
      Name = "Receiving facility",
      Address = "1 Main Street",
      City = "Port St. Lucie",
      Province = "FL",
      Country = "US",
      TruckNumber = truck.UnitNumber,
      ScheduledDate = new(2026, 9, 14),
      ScheduledTime = new(5, 0),
    };
    f.Sources.Add(
      new()
      {
        LoadNumber = 1383,
        TruckNumber = truck.UnitNumber,
        Status = "in_transit",
        Stops = [stop],
      }
    );
    await f.Handler.Handle(new(), default);
    var load = await f.Db.Dispatches.Include(x => x.Stops).SingleAsync();
    var saved = Assert.Single(load.Stops);
    var workspace = new DispatchWorkspace
    {
      Id = load.Id,
      OwnsStops = true,
      SourceStopsJson = ExecutionSnapshots.Write(load.Stops),
    };
    saved.Notes = "Local instructions";
    if (localAppointment)
      saved.ScheduledDate = new(2026, 9, 18);
    var trip = new Trip { Id = Guid.NewGuid() };
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TripId = trip.Id,
      TruckId = truck.Id,
      Status = "active",
      Revision = 1,
      SourceAssignmentSignature = (
        await f.Db.DispatchSourceLinks.SingleAsync()
      ).AssignmentSignature,
      StartedAt = DateTime.UtcNow.AddDays(-1),
      Stops = ExecutionStopRows.Capture(load.Stops),
    };
    leg.Loads.Add(
      new()
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        ExecutionLegId = leg.Id,
        Sequence = 1,
        StartVisitId = saved.Id,
        EndVisitId = saved.Id,
      }
    );
    f.Db.AddRange(workspace, trip, leg);
    await f.Db.SaveChangesAsync();
    stop.ScheduledDate = new(2026, 9, 16);
    stop.ScheduledTime = new(9, 0);
    stop.ArrivedAt = DateTime.UtcNow.AddMinutes(-10);
    await f.Handler.Handle(new(), default);

    var updated = Assert.Single(ExecutionStopRows.Read(leg));
    Assert.Equal(saved.Id, updated.Id);
    Assert.Equal(
      new DateOnly(2026, 9, localAppointment ? 18 : 16),
      updated.ScheduledDate
    );
    Assert.Equal(
      new TimeOnly(localAppointment ? 5 : 9, 0),
      updated.ScheduledTime
    );
    Assert.Equal(stop.ArrivedAt, updated.ArrivedAt);
    Assert.Equal("Local instructions", saved.Notes);
    Assert.Equal(truck.Id, leg.TruckId);
    Assert.Null(leg.SourceReviewReason);
    Assert.Equal(2, leg.Revision);
    var work = Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
    Assert.Equal(leg.Id, work.ExecutionLegId);
    Assert.Equal(2, work.AssignmentRevision);
    await f.Handler.Handle(new(), default);
    Assert.Equal(2, leg.Revision);
    Assert.Single(await f.Db.ExecutionPlanningChanges.ToListAsync());
  }
}
