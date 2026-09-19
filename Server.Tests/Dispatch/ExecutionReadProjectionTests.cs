using Application.Features.Dispatch.Queries;
using Application.Features.Execution.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Entities.Fleet;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionReadProjectionTests
{
  [Fact]
  public void AcceptedProjectionCannotMutateSourceVisitsOrKeepOldResources()
  {
    var source = new Load
    {
      Id = Guid.NewGuid(),
      LoadNumber = 481,
      Price = 1200,
      Currency = "USD",
      CustomerName = "Customer",
      TruckId = Guid.NewGuid(),
      TruckNumber = "old-truck",
      DriverId = Guid.NewGuid(),
      DriverName = "Old driver",
      Driver = new Driver(),
      TrailerId = Guid.NewGuid(),
      TrailerNumber = "old-trailer",
      PlanningTruckId = Guid.NewGuid(),
      PlanningFromStopId = Guid.NewGuid(),
      PlanningAssignmentRevision = 17,
      Stops = [new() { Id = Guid.NewGuid(), Notes = "Retained" }],
    };
    source.Stops[0].Dispatch = source;
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      DriverId = Guid.NewGuid(),
      TrailerId = Guid.NewGuid(),
      Status = "active",
      Revision = 4,
    };
    var first = ExecutionLoadProjection.Capture(source, leg, source.Stops);
    var second = ExecutionLoadProjection.Capture(source, leg, source.Stops);
    first = first with
    {
      Work = first.Work with
      {
        Stops = first.Work.Stops.SetItem(
          0,
          first.Work.Stops[0] with
          {
            Notes = "Only the first consumer",
          }
        ),
      },
    };
    source.Stops[0].Notes = "Later source edit";

    Assert.Equal("Only the first consumer", first.Work.Stops[0].Notes);
    Assert.Equal("Retained", second.Work.Stops[0].Notes);
    Assert.Equal(leg.DriverId, second.Work.DriverId);
    Assert.Equal(leg.TruckId, second.Work.TruckId);
    Assert.Equal(leg.TrailerId, second.Work.TrailerId);
    Assert.Empty(second.Details.DriverName);
    Assert.Empty(second.Work.TruckNumber);
    Assert.Empty(second.Details.TrailerNumber);
    Assert.Null(second.Work.PlanningTruckId);
    Assert.Null(second.Work.PlanningFromStopId);
    Assert.Equal(0, second.Work.PlanningAssignmentRevision);
    Assert.Equal(481, second.Work.LoadNumber);
    Assert.Equal(1200, second.Work.Price);
    Assert.Equal("USD", second.Work.Currency);
    Assert.Equal("Customer", second.Details.CustomerName);
    Assert.Equal(4, second.Work.AssignmentRevision);
  }

  [Fact]
  public void DetachedProjectionRetainsTransferReadinessAndUnknownActualTime()
  {
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Job = "Receive",
      ExecutionCompleted = true,
      AwaitingHandoff = true,
    };
    var projected = ExecutionLoadProjection.Capture(new(), new(), [stop]);
    var copied = Assert.Single(projected.Work.Stops);
    Assert.True(copied.AwaitingHandoff);
    Assert.False(copied.IsCompleted);
    Assert.Null(copied.ManualCompletedAt);
    copied = copied with { AwaitingHandoff = false };
    Assert.True(copied.IsCompleted);
    Assert.True(stop.AwaitingHandoff);
  }

  [Fact]
  public void DisplayRetainsAcceptedCargoAndDetachedAppointmentDetails()
  {
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Job = "Hook",
      StateAfter = "Loaded",
      ScheduledDate = new(2026, 9, 17),
      ScheduledTime = new(9, 0),
      ScheduledDate2 = new(2026, 9, 18),
      ScheduledTime2 = new(11, 0),
      IsWindow = true,
      AppointmentTimeZoneId = "America/Toronto",
      Commodity = "Goods",
      Notes = "Keep upright",
      Weight = 2000,
      WeightUnit = "lb",
      DriverName = "Accepted driver",
      ExecutionCompleted = true,
    };
    var captured = ExecutionLoadProjection.Capture(
      new() { CustomerName = "Customer" },
      new() { Id = Guid.NewGuid() },
      [stop]
    );
    stop.StateAfter = "Empty";
    stop.Weight = 1;
    stop.ScheduledDate2 = null;
    var display = DispatchProjection.FromExecution(captured);
    var actual = Assert.Single(display.Stops);
    Assert.Equal("Hook", actual.Job);
    Assert.Equal("Loaded", actual.StateAfter);
    Assert.Equal(new DateOnly(2026, 9, 18), actual.ScheduledDate2);
    Assert.Equal(new TimeOnly(11, 0), actual.ScheduledTime2);
    Assert.True(actual.IsWindow);
    Assert.Equal("America/Toronto", actual.AppointmentTimeZoneId);
    Assert.Equal("Goods", actual.Commodity);
    Assert.Equal("Keep upright", actual.Notes);
    Assert.Equal(2000, actual.Weight);
    Assert.Equal("lb", actual.WeightUnit);
    Assert.Equal("Accepted driver", actual.DriverName);
    Assert.True(actual.IsCompleted);
    Assert.Null(actual.ManualCompletedAt);
    actual.Notes = "Display edit";
    Assert.Equal("Keep upright", captured.Work.Stops[0].Notes);
  }
}
