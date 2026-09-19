using System.Globalization;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchWorkspaceRulesTests
{
  [Fact]
  public void CrossDayWindowAcceptsEndAfterStartAndRejectsReverse()
  {
    var stop = Stop();
    stop.AppointmentMode = "window";
    stop.TimeZoneId = "America/Toronto";
    stop.ScheduledDate = new(2026, 9, 15);
    stop.ScheduledTime = new(22, 0);
    stop.ScheduledDate2 = new(2026, 9, 16);
    stop.ScheduledTime2 = new(6, 0);
    Assert.Null(DispatchWorkspaceRules.ValidateStop(stop));
    stop.ScheduledDate2 = new(2026, 9, 15);
    Assert.NotNull(DispatchWorkspaceRules.ValidateStop(stop));
  }

  [Fact]
  public void UnscheduledAndClockGapCannotCarryInvalidTimes()
  {
    var stop = Stop();
    Assert.Null(DispatchWorkspaceRules.ValidateStop(stop));
    stop.ScheduledDate = new(2026, 3, 8);
    Assert.NotNull(DispatchWorkspaceRules.ValidateStop(stop));
    stop.AppointmentMode = "at";
    stop.ScheduledTime = new(2, 30);
    stop.TimeZoneId = "America/Toronto";
    Assert.NotNull(DispatchWorkspaceRules.ValidateStop(stop));
  }

  [Fact]
  public void CannotCrossRecordedOrAssignmentSegment()
  {
    var first = Stop();
    first.CanMove = first.CanEdit = first.CanRemove = true;
    first.SegmentKey = "leg:1";
    var second = Stop();
    second.SegmentKey = "leg:2";
    var response = new DispatchWorkspaceResponse { Stops = [first, second] };
    var request = new UpdateDispatchWorkspaceRequest
    {
      Stops = [second, first],
    };
    Assert.NotNull(DispatchWorkspaceRules.Validate(response, request));
  }

  [Fact]
  public void ImportKeepsLocalAddressAndDoesNotAttachOldVisitActuals()
  {
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Sequence = 1,
      Job = "Delivery",
      Name = "Facility",
      Address = "1 Main St",
      City = "Fixture",
      Country = "US",
      Latitude = 40m,
      Longitude = -80m,
    };
    var load = new DispatchEntity { Stops = [stop] };
    var workspace = new DispatchWorkspace
    {
      OwnsStops = true,
      SourceStopsJson = ExecutionSnapshots.Write(load.Stops),
    };
    stop.Address = "2 New St";
    DispatchWorkspaceImport.MergeStops(
      load,
      workspace,
      [
        new ExternalDispatchStop
        {
          Sequence = 1,
          Job = "Delivery",
          Name = "Facility",
          Address = "1 Main St",
          City = "Fixture",
          Country = "US",
          Latitude = 40m,
          Longitude = -80m,
          DeliveredAt = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc),
        },
      ],
      new(2026, 9, 15, 13, 0, 0, DateTimeKind.Utc)
    );
    Assert.Equal("2 New St", stop.Address);
    Assert.Null(stop.DeliveredAt);
    Assert.NotNull(workspace.SourceReviewReason);
  }

  [Theory]
  [InlineData("10000000000000000")]
  [InlineData("1.001")]
  [InlineData("-1")]
  public void CargoRejectsValuesOutsidePersistencePrecision(string value)
  {
    var stop = Stop();
    stop.Pallets = decimal.Parse(value, CultureInfo.InvariantCulture);
    Assert.NotNull(DispatchWorkspaceRules.ValidateStop(stop));
  }

  private static DispatchWorkspaceStop Stop() =>
    new()
    {
      Id = Guid.NewGuid(),
      Job = "Delivery",
      Name = "Facility",
      Address = "1 Main St",
      City = "Fixture",
      Country = "US",
      Latitude = 40m,
      Longitude = -80m,
    };
}
