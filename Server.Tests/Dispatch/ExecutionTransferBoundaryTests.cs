using Application.Features.Execution.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Execution;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ExecutionTransferBoundaryTests
{
  [Fact]
  public void SnapshotRoundTripPreservesAddressVerificationRetryState()
  {
    var stop = new DispatchStop
    {
      Id = Guid.NewGuid(),
      Address = "123 Main Street",
      SourceAddressJson = "{\"address\":\"123 Main Street\"}",
      AddressVerifiedAt = DateTime.UtcNow.AddDays(-1),
      AddressRetryAfter = DateTime.UtcNow.AddHours(1),
    };
    var leg = new ExecutionLeg { Stops = ExecutionStopRows.Capture([stop]) };
    var retained = Assert.Single(ExecutionStopRows.Read(leg));
    Assert.Equal(stop.SourceAddressJson, retained.SourceAddressJson);
    Assert.Equal(stop.AddressVerifiedAt, retained.AddressVerifiedAt);
    Assert.Equal(stop.AddressRetryAfter, retained.AddressRetryAfter);
  }

  [Theory]
  [InlineData("Drop", "Loaded", "Bobtail")]
  [InlineData("Hook", "Loaded", "Loaded")]
  [InlineData("Hook", "Unknown", "Unknown")]
  [InlineData("Release", "Empty", "Empty")]
  [InlineData("Receive", "Loaded", "Loaded")]
  public void IndependentTransferRetainsExplicitCargo(
    string operation,
    string before,
    string expected
  )
  {
    var visit = new ExecutionTransferVisit
    {
      Id = Guid.NewGuid(),
      Operation = operation,
      SiteName = "123 Transfer Yard",
    };
    var boundary = ExecutionSnapshots.Boundary(
      visit,
      Guid.NewGuid(),
      0,
      before
    );
    Assert.Equal(expected, boundary.StateAfter);
    Assert.Equal(visit.SiteName, boundary.Address);
    Assert.False(boundary.IsCompleted);
    Assert.Null(boundary.ArrivedAt);
  }
}
