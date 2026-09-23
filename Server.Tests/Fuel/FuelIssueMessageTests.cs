using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

// The words a driver is handed. Every place is a stop of the itinerary or
// the station; the road the truck is on is placed by distance, a later one
// between the stops it falls between.
[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelIssueMessageTests
{
  private static readonly Guid First = Guid.NewGuid();
  private static readonly Guid Second = Guid.NewGuid();
  private static readonly PlanStop Delivery = Stop("Delivery", "Acme DC");
  private static readonly PlanStop Pickup = Stop("Pickup", "Bolt Mill");
  private static readonly PlanStop Drop = Stop("Delivery", "Cargo Yard");

  private static readonly IReadOnlyList<FuelItineraryStop> Itinerary =
  [
    new(First, Delivery, 300),
    new(Second, Pickup, 700),
    new(Second, Drop, 1200),
  ];

  [Fact]
  public void TheRoadTheTruckIsOnIsPlacedByDistance()
  {
    var line = FuelIssueMessage.Line(
      Visit(First, Delivery, miles: 118.6, fill: true),
      Itinerary,
      First,
      Delivery.Id
    );
    Assert.Equal(
      "Ahead, about 119 mi before your delivery at Acme DC: fuel at "
        + "LOVES #706, 3499 Lee Jackson Hwy - fill the tank.",
      line
    );
  }

  [Fact]
  public void ALaterRoadIsPlacedAfterADeliveryOnTheWayToTheNextPickup()
  {
    var line = FuelIssueMessage.Line(
      Visit(Second, Pickup, gallons: 84.6),
      Itinerary,
      First,
      Delivery.Id
    );
    Assert.Equal(
      "After your delivery at Acme DC, on the way to your pickup at Bolt "
        + "Mill: fuel at LOVES #706, 3499 Lee Jackson Hwy - 85 gal.",
      line
    );
  }

  // The same station twice is two visits, each placed on its own road.
  [Fact]
  public void AStationVisitedTwiceIsTwoLinesAndTwoVisits()
  {
    var early = Visit(Second, Pickup, gallons: 40);
    var late = Visit(Second, Drop, gallons: 60);
    late.StationId = early.StationId;

    Assert.NotEqual(FuelVisitIdentity.Key(early), FuelVisitIdentity.Key(late));
    Assert.StartsWith(
      "After your delivery at Acme DC",
      FuelIssueMessage.Line(early, Itinerary, First, Delivery.Id)
    );
    Assert.StartsWith(
      "After your pickup at Bolt Mill, on the way to your delivery at Cargo Yard",
      FuelIssueMessage.Line(late, Itinerary, First, Delivery.Id)
    );
  }

  [Fact]
  public void APumpSellingLitresIsToldInLitresToo()
  {
    var visit = Visit(Second, Pickup, gallons: 50);
    visit.Unit = "L";
    Assert.EndsWith(
      "- 50 gal (about 189 L).",
      FuelIssueMessage.Line(visit, Itinerary, First, Delivery.Id)
    );
  }

  [Fact]
  public void NoTimeIsGivenAndAnEmptyShiftSaysSo()
  {
    var visit = Visit(Second, Pickup, gallons: 50);
    visit.EstimatedArrival = DateTimeOffset.UtcNow.AddHours(3);
    Assert.DoesNotContain(
      ":00",
      FuelIssueMessage.Line(visit, Itinerary, First, Delivery.Id)
    );
    Assert.Equal(
      "No fuel stop is planned for this shift.",
      FuelIssueMessage.Compose([])
    );
    Assert.Equal(
      "Fuel for this shift:\n1. a\n2. b",
      FuelIssueMessage.Compose(["a", "b"])
    );
  }

  [Fact]
  public void AFillIsTheSameHandOverWhateverTheGallonsComeTo()
  {
    var fill = Visit(First, Delivery, fill: true);
    fill.BuyGallons = 180;
    var before = FuelVisitIdentity.Content(fill);
    fill.BuyGallons = 162;
    Assert.Equal(before, FuelVisitIdentity.Content(fill));
    Assert.Equal("full", before);
    var stated = Visit(First, Delivery, gallons: 84.5);
    Assert.Equal("85", FuelVisitIdentity.Content(stated));
  }

  private static PlanStop Stop(string job, string name) =>
    new(Guid.NewGuid(), name, $"{name} address", 1, new(40, -80))
    {
      Job = job,
    };

  private static FuelPlanStop Visit(
    Guid dispatch,
    PlanStop before,
    double miles = 50,
    bool fill = false,
    double gallons = 0
  ) =>
    new()
    {
      StationId = Guid.NewGuid(),
      Name = "LOVES #706",
      Address = "3499 Lee Jackson Hwy",
      DispatchId = dispatch,
      BeforeStopId = before.Id,
      MilesAhead = miles,
      FillToTarget = fill,
      BuyGallons = gallons,
      Unit = "US gal",
    };
}
