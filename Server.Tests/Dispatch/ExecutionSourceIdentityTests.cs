using Application.Features.Execution.Models;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionSourceIdentityTests
{
  [Theory]
  [InlineData("truck")]
  [InlineData("driver")]
  [InlineData("trailer")]
  [InlineData("unresolved-truck")]
  [InlineData("unresolved-driver")]
  [InlineData("unresolved-trailer")]
  public void HeaderAssignmentChangeInvalidatesTheReviewedSource(string field)
  {
    var source = new Load { Id = Guid.NewGuid() };
    var reviewed = ExecutionSnapshots.Fingerprint(source);
    switch (field)
    {
      case "truck":
        source.TruckId = Guid.NewGuid();
        break;
      case "driver":
        source.DriverId = Guid.NewGuid();
        break;
      case "trailer":
        source.TrailerId = Guid.NewGuid();
        break;
      case "unresolved-truck":
        source.TruckNumber = "Unknown truck";
        break;
      case "unresolved-driver":
        source.DriverName = "Unknown driver";
        break;
      case "unresolved-trailer":
        source.TrailerNumber = "Unknown trailer";
        break;
    }

    Assert.NotEqual(reviewed, ExecutionSnapshots.Fingerprint(source));
  }

  [Theory]
  [InlineData("truck")]
  [InlineData("driver")]
  [InlineData("co-driver")]
  [InlineData("trailer")]
  public void UnmatchedStopResourceStillInvalidatesTheReviewedSource(
    string field
  )
  {
    var source = new Load
    {
      Id = Guid.NewGuid(),
      Stops = [new() { Id = Guid.NewGuid(), Sequence = 1 }],
    };
    var reviewed = ExecutionSnapshots.Fingerprint(source);
    var stop = source.Stops[0];
    switch (field)
    {
      case "truck":
        stop.TruckNumber = "Unknown truck";
        break;
      case "driver":
        stop.DriverName = "Unknown driver";
        break;
      case "co-driver":
        stop.CoDriverName = "Unknown co-driver";
        break;
      case "trailer":
        stop.TrailerNumber = "Unknown trailer";
        break;
    }

    Assert.NotEqual(reviewed, ExecutionSnapshots.Fingerprint(source));
  }
}
