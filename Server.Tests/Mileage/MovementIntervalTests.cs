using Application.Features.Mileage.Services;
using Domain.Entities.Mileage;

namespace Server.Tests.Mileage;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class MovementIntervalTests
{
  private static readonly Guid Truck = Guid.NewGuid();
  private static readonly DateTime Start = new(
    2026,
    9,
    8,
    8,
    0,
    0,
    DateTimeKind.Utc
  );

  [Theory]
  [InlineData(-2, -1, false)]
  [InlineData(-1, 0, false)]
  [InlineData(-1, 1, true)]
  [InlineData(0, 2, true)]
  [InlineData(1, 3, true)]
  [InlineData(2, 3, false)]
  [InlineData(3, 4, false)]
  public void PredicateRejectsOverlapButAllowsAdjacentIntervals(
    int startHour,
    int endHour,
    bool overlaps
  )
  {
    var predicate = MovementIntervals
      .OverlappingTruck(Truck, Start, Start.AddHours(2))
      .Compile();
    Assert.Equal(
      overlaps,
      predicate(
        new Movement
        {
          TruckId = Truck,
          StartedAt = Start.AddHours(startHour),
          EndedAt = Start.AddHours(endHour),
        }
      )
    );
  }

  [Fact]
  public void PlannedUnscheduledAndAnotherTruckDoNotConflict()
  {
    var predicate = MovementIntervals
      .OverlappingTruck(Truck, Start, Start.AddHours(2))
      .Compile();
    Assert.False(predicate(new() { TruckId = Truck, PlannedMiles = 100 }));
    Assert.False(
      predicate(
        new()
        {
          TruckId = Guid.NewGuid(),
          StartedAt = Start,
          EndedAt = Start.AddHours(2),
        }
      )
    );
    Assert.False(
      MovementIntervals
        .OverlappingTruck(Truck, null, null)
        .Compile()(new() { TruckId = Truck, StartedAt = Start })
    );
  }

  [Fact]
  public void OpenIntervalConflictsButCurrentMovementIsExcluded()
  {
    var movement = new Movement
    {
      Id = Guid.NewGuid(),
      TruckId = Truck,
      StartedAt = Start.AddHours(-1),
    };
    Assert.True(
      MovementIntervals
        .OverlappingTruck(Truck, Start, Start.AddHours(2))
        .Compile()(movement)
    );
    Assert.False(
      MovementIntervals
        .OverlappingTruck(Truck, Start, Start.AddHours(2), movement.Id)
        .Compile()(movement)
    );
  }

  [Fact]
  public void ActualEvidenceNeedsFinishedObservedInterval()
  {
    var end = Start.AddHours(1);
    var now = end.AddHours(1);
    Assert.True(MovementIntervals.ValidActual(Start, end, end, now));
    Assert.False(MovementIntervals.ValidActual(Start, null, end, now));
    Assert.False(MovementIntervals.ValidActual(null, end, end, now));
    Assert.False(MovementIntervals.ValidActual(end, end, end, now));
    Assert.False(MovementIntervals.ValidActual(Start, end, Start, now));
    Assert.False(
      MovementIntervals.ValidActual(Start, end, now.AddMinutes(1), now)
    );
  }
}
