using Application.Features.Mileage.Models;
using Application.Features.Mileage.Services;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Server.Tests.Mileage;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class OdometerEvidenceTests
{
  private static readonly DateTime Start = new(
    2026,
    9,
    13,
    10,
    0,
    0,
    DateTimeKind.Utc
  );

  [Fact]
  public void CumulativeMeasuredMetersRemainSeparateFromRoadMiles()
  {
    Assert.Null(
      OdometerEvidence.Validate(Start, Start.AddHours(1), 100_000m, 116_093.44m)
    );
    Assert.Equal(10m, OdometerEvidence.Miles(100_000m, 116_093.44m));
    Assert.Equal(0m, OdometerEvidence.Miles(100_000m, 100_000m));
  }

  [Theory]
  [InlineData(60, 1000, 999, "odometer-reset-or-invalid-reading")]
  [InlineData(0, 1000, 1000, "non-increasing-sample-time")]
  [InlineData(421, 1000, 2000, "odometer-sample-gap")]
  [InlineData(1, 1000, 100000, "implausible-odometer-increase")]
  public void MissingResetAndImplausibleSamplesCannotBecomeActual(
    int minutes,
    decimal before,
    decimal after,
    string reason
  )
  {
    Assert.Equal(
      reason,
      OdometerEvidence.Validate(Start, Start.AddMinutes(minutes), before, after)
    );
  }

  [Fact]
  public void CaptureRequiresBothFactualCargoBoundaries()
  {
    var leg = Leg();
    var stops = Stops();
    stops[1].ArrivedAt = null;
    stops[1].ScheduledDate = DateOnly.FromDateTime(Start);
    stops[1].ScheduledTime = new(12, 0);
    Assert.Equal(
      -1,
      OdometerEvidence.Segment(
        leg,
        stops,
        Start.AddMinutes(5),
        Start.AddMinutes(10)
      )
    );
    stops[1].ArrivedAt = Start.AddHours(2);
    Assert.Equal(
      0,
      OdometerEvidence.Segment(
        leg,
        stops,
        Start.AddMinutes(5),
        Start.AddMinutes(10)
      )
    );
  }

  [Fact]
  public void BootstrapAndAssignmentBoundariesNeverBackfillGuessedWork()
  {
    var leg = Leg();
    leg.RecordedAt = Start.AddMinutes(30);
    Assert.Equal(
      -1,
      OdometerEvidence.Segment(
        leg,
        Stops(),
        Start.AddMinutes(5),
        Start.AddMinutes(10)
      )
    );
    Assert.Equal(
      0,
      OdometerEvidence.Segment(
        leg,
        Stops(),
        Start.AddMinutes(30),
        Start.AddMinutes(40)
      )
    );
    leg.CompletedAt = Start.AddMinutes(35);
    Assert.Equal(
      -1,
      OdometerEvidence.Segment(
        leg,
        Stops(),
        Start.AddMinutes(30),
        Start.AddMinutes(40)
      )
    );
  }

  [Fact]
  public void HandoffAndSourceReviewBlockAutomaticAttribution()
  {
    var leg = Leg();
    var stops = Stops();
    stops[1].AwaitingHandoff = true;
    Assert.Equal(
      -1,
      OdometerEvidence.Segment(
        leg,
        stops,
        Start.AddMinutes(5),
        Start.AddMinutes(10)
      )
    );
    stops[1].AwaitingHandoff = false;
    leg.SourceReviewReason = "changed-after-confirmation";
    Assert.Equal(
      -1,
      OdometerEvidence.Segment(
        leg,
        stops,
        Start.AddMinutes(5),
        Start.AddMinutes(10)
      )
    );
  }

  [Fact]
  public void MultipleCarriedLoadsDoNotDuplicateOnePhysicalMovement()
  {
    var leg = Leg();
    var stops = Stops();
    leg.Loads = Enumerable
      .Range(0, 2)
      .Select(_ => new LoadExecutionLeg
      {
        DispatchId = Guid.NewGuid(),
        StartVisitId = stops[0].Id,
        EndVisitId = stops[1].Id,
      })
      .ToList();
    var scope = MileageSegmentScope.Create(leg, stops, 0);
    Assert.Null(scope.CarriedDispatchId);
    var allocation = MileageAllocation.Resolve(
      scope.Movement("samsara-obd", Guid.NewGuid(), "hash", Start),
      new()
    );
    Assert.Equal("unallocated", allocation.Target);
  }

  [Fact]
  public void AutomaticBasesDoNotDoubleCountOrPoisonEachOther()
  {
    var rows = new[]
    {
      Row("native-route", 100, null),
      Row("samsara-obd", null, 20),
      Row("samsara-obd", null, 30),
    };
    Assert.Equal(100m, MileageSummation.Sum(rows, false).TotalMiles);
    Assert.Equal(50m, MileageSummation.Sum(rows, true).TotalMiles);
    Assert.Equal(0, MileageSummation.Sum(rows, true).MissingEvidence);
  }

  private static ExecutionLeg Leg() =>
    new()
    {
      Id = Guid.NewGuid(),
      TruckId = Guid.NewGuid(),
      TrailerId = Guid.NewGuid(),
      Status = "active",
      StartedAt = Start,
      RecordedAt = Start.AddDays(-1),
    };

  private static DispatchStop[] Stops() =>
    [
      new()
      {
        Id = Guid.NewGuid(),
        Job = "Pick Up",
        StateAfter = "Loaded",
        DepartedAt = Start,
      },
      new()
      {
        Id = Guid.NewGuid(),
        Job = "Drop Off",
        ArrivedAt = Start.AddHours(2),
      },
    ];

  private static MileageMovementRow Row(
    string origin,
    decimal? planned,
    decimal? actual
  ) =>
    new(
      Guid.NewGuid(),
      1,
      null,
      null,
      null,
      null,
      "delivery",
      "loaded",
      null,
      null,
      null,
      null,
      "carried",
      "test",
      0,
      false,
      origin,
      planned,
      actual,
      null,
      null,
      true
    )
    {
      Origin = origin,
      CanEditDistance = false,
    };
}
