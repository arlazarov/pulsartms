using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Domain.Entities.Mileage;

namespace Server.Tests.Mileage;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class MileagePolicyTests
{
  private static readonly Guid Previous = Guid.NewGuid();
  private static readonly Guid Next = Guid.NewGuid();
  private static readonly Guid Carried = Guid.NewGuid();

  [Theory]
  [InlineData("home")]
  [InlineData("yard-return")]
  [InlineData("maintenance")]
  [InlineData("reposition")]
  [InlineData("pickup-approach")]
  public void LoadedMovementFollowsExplicitCarriedLoad(string purpose)
  {
    var allocation = MileageAllocation.Resolve(
      Movement(purpose, "loaded"),
      Policy()
    );
    Assert.Equal(Carried, allocation.DispatchId);
    Assert.Equal("carried", allocation.Target);
  }

  [Theory]
  [InlineData("empty")]
  [InlineData("bobtail")]
  [InlineData("unknown")]
  public void ExplicitPickupApproachFollowsNextLoad(string cargo)
  {
    var allocation = MileageAllocation.Resolve(
      Movement("pickup-approach", cargo),
      Policy()
    );
    Assert.Equal(Next, allocation.DispatchId);
    Assert.Equal("pickup-approach", allocation.Reason);
  }

  [Theory]
  [InlineData("home", "previous")]
  [InlineData("yard-return", "next")]
  [InlineData("maintenance", "unallocated")]
  [InlineData("reposition", "previous")]
  public void NonLoadedPurposeUsesItsOwnCompanyRule(
    string purpose,
    string target
  )
  {
    var allocation = MileageAllocation.Resolve(
      Movement(purpose, "bobtail"),
      Policy()
    );
    Assert.Equal(target, allocation.Target);
  }

  [Fact]
  public void MissingCarriedLoadNeverGuessesFromPreviousOrNext()
  {
    var movement = new Movement
    {
      CargoState = "loaded",
      Purpose = "delivery",
      PreviousDispatchId = Previous,
      NextDispatchId = Next,
    };
    var allocation = MileageAllocation.Resolve(movement, Policy());
    Assert.Null(allocation.DispatchId);
    Assert.Equal("unallocated", allocation.Target);
    Assert.Equal("missing-carried-load", allocation.Reason);
  }

  [Fact]
  public void ManualTargetUsesOnlyItsExplicitContext()
  {
    var movement = Movement("delivery", "loaded");
    var allocation = MileageAllocation.ForTarget(
      movement,
      "previous",
      "Dispatcher confirmed exception"
    );
    Assert.Equal(Previous, allocation.DispatchId);
    Assert.Equal("previous", allocation.Target);
    Assert.Equal("Dispatcher confirmed exception", allocation.Reason);
  }

  [Fact]
  public void BobtailIsAnEmptySubsetAndNeverAddedToTotalTwice()
  {
    var totals = MileageSummation.Sum(
      [
        Row("loaded", 100),
        Row("empty", 20),
        Row("bobtail", 10),
        Row("unknown", 5),
      ],
      false
    );
    Assert.Equal(100m, totals.LoadedMiles);
    Assert.Equal(30m, totals.EmptyMiles);
    Assert.Equal(10m, totals.BobtailMiles);
    Assert.Equal(5m, totals.UnknownMiles);
    Assert.Equal(135m, totals.TotalMiles);
  }

  [Fact]
  public void PlannedMilesNeverBecomeActualEvidence()
  {
    var totals = MileageSummation.Sum([Row("empty", 42)], true);
    Assert.Null(totals.EmptyMiles);
    Assert.Null(totals.TotalMiles);
    Assert.Equal(1, totals.MissingEvidence);
  }

  [Fact]
  public void MissingOrTruncatedEvidenceDoesNotLookComplete()
  {
    Assert.Null(MileageSummation.Sum([], false).TotalMiles);
    var rows = new[] { Row("empty", 10), Row("loaded", null) };
    var partial = MileageSummation.Sum(rows, false);
    Assert.Equal(10m, partial.EmptyMiles);
    Assert.Null(partial.LoadedMiles);
    Assert.Null(partial.TotalMiles);
    var truncated = MileageSummation.Sum(rows, false, true);
    Assert.Null(truncated.EmptyMiles);
    Assert.Null(truncated.TotalMiles);
  }

  private static Movement Movement(string purpose, string cargo) =>
    new()
    {
      Purpose = purpose,
      CargoState = cargo,
      PreviousDispatchId = Previous,
      NextDispatchId = Next,
      CarriedDispatchId = Carried,
    };

  private static MileageAllocationPolicy Policy() =>
    new()
    {
      Home = "previous",
      YardReturn = "next",
      Maintenance = "unallocated",
      Reposition = "previous",
    };

  private static MileageMovementRow Row(string cargo, decimal? planned) =>
    new(
      MovementId: Guid.NewGuid(),
      Revision: 1,
      TruckId: null,
      DriverId: null,
      CoDriverId: null,
      TrailerId: null,
      Purpose: "reposition",
      CargoState: cargo,
      PreviousDispatchId: null,
      NextDispatchId: null,
      CarriedDispatchId: null,
      AllocatedDispatchId: null,
      AllocationTarget: "unallocated",
      AllocationReason: "test",
      PolicyRevision: 0,
      ManualOverride: false,
      Source: "recorded-movement",
      PlannedMiles: planned,
      ActualMiles: null,
      PlannedAt: null,
      ActualAt: null,
      Editable: true
    );
}
