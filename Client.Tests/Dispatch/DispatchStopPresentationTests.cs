using Client.Models.DTO.Dispatch;
using Client.Shared.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchStopPresentationTests
{
  [Theory]
  [InlineData("Driver start", "Unknown", false)]
  [InlineData("Collect truck", "Unknown", false)]
  [InlineData("Collect trailer", "Empty", false)]
  [InlineData("Drop trailer", "Bobtail", false)]
  [InlineData("Waypoint", "Empty", false)]
  [InlineData("Waypoint", "Loaded", true)]
  [InlineData("Waypoint", "Unknown", true)]
  [InlineData("Pick Up", "Loaded", true)]
  [InlineData("Drop Off", "Empty", true)]
  [InlineData("Delivery", "Unknown", true)]
  public void CargoVisibilityUsesOperationWithoutInferringMissingCargo(
    string job,
    string state,
    bool expected
  )
  {
    var stop = new DispatchStopResponse
    {
      Job = job,
      StateAfter = state,
      Commodity = "Food",
      Weight = 43063,
    };
    Assert.Equal(expected, DispatchStopPresentation.ShowsCargo(stop));
    stop.DriverOnly = true;
    Assert.False(DispatchStopPresentation.ShowsCargo(stop));
  }

  [Fact]
  public void RepeatedAddressesRetainEveryIdentityAndGetVisitNumbersInRouteOrder()
  {
    var first = Stop(1, "1886 Tebor Rd");
    var second = Stop(2, "1730 NY-5S");
    var third = Stop(3, "  1886  tebor rd  ");
    var fourth = Stop(4, "1886 Tebor Rd");
    var delivery = Stop(5, "13077 SW Anthony F. Sansone Sr. Blvd");
    third.Name = "Provider spelling changed";
    first.PickedUpAt = new(2026, 9, 11, 2, 15, 0);

    var visits = DispatchStopPresentation.OrderedVisits(
      [fourth, delivery, second, third, first]
    );

    Assert.Equal(
      new[] { first.Id, second.Id, third.Id, fourth.Id, delivery.Id },
      visits.Select(visit => visit.Stop.Id)
    );
    Assert.Equal(new[] { 1, 2, 3, 4, 5 }, visits.Select(visit => visit.Number));
    Assert.Equal(
      new[] { "Visit 1 of 3", "", "Visit 2 of 3", "Visit 3 of 3", "" },
      visits.Select(visit => visit.VisitLabel)
    );
    Assert.Same(first, visits[0].Stop);
  }

  [Theory]
  [InlineData("", "Webster", "NY", "")]
  [InlineData("Warehouse Road", "", "NY", "")]
  [InlineData("Warehouse Road", "Webster", "", "")]
  public void IncompleteAddressesAndCoincidentCoordinatesDoNotClaimRepeatedVisits(
    string address,
    string city,
    string province,
    string zip
  )
  {
    var stops = new[] { Stop(1, address), Stop(2, address) };
    foreach (var stop in stops)
    {
      stop.City = city;
      stop.Province = province;
      stop.ZipCode = zip;
      stop.Latitude = 43;
      stop.Longitude = -77;
    }
    Assert.All(
      DispatchStopPresentation.OrderedVisits(stops),
      visit => Assert.Empty(visit.VisitLabel)
    );
  }

  [Fact]
  public void SameFacilityNameOrStreetInAnotherCityDoesNotCreateARepeat()
  {
    var first = Stop(1, "Warehouse Road");
    var second = Stop(2, "Warehouse Road");
    second.City = "Amsterdam";
    var third = Stop(3, "Different Street");
    Assert.All(
      DispatchStopPresentation.OrderedVisits([first, second, third]),
      visit => Assert.Empty(visit.VisitLabel)
    );
  }

  [Theory]
  [InlineData("Pickup", "Delivery")]
  [InlineData("Pick Up", "Drop Off")]
  [InlineData(" pickup ", "dropoff")]
  public void SummaryCountsJobsWithoutCollapsingRepeatedStops(
    string pickup,
    string delivery
  )
  {
    var stops = Enumerable
      .Range(1, 5)
      .Select(number => Stop(number, "1886 Tebor Rd"))
      .ToArray();
    foreach (var stop in stops)
      stop.Job = stop.Sequence == 5 ? delivery : pickup;
    Assert.Equal(
      "5 stops · 4 pickups · 1 delivery",
      DispatchStopPresentation.Summary(stops)
    );
    Assert.Equal(
      "1 stop · 1 delivery",
      DispatchStopPresentation.Summary([stops[^1]])
    );
    Assert.Equal("0 stops", DispatchStopPresentation.Summary([]));
  }

  private static DispatchStopResponse Stop(int sequence, string address) =>
    new()
    {
      Id = Guid.NewGuid(),
      Sequence = sequence,
      Name = "Distribution centre",
      Address = address,
      City = "Webster",
      Province = "NY",
      Country = "US",
    };
}
