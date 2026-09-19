using Client.Models.DTO.Fleet;
using Client.Pages.FleetMap;

namespace Client.Tests.Fleet;

[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public class TruckMapSearchTests
{
  private static List<TruckLocationMapDto> Trucks =>
    [
      new() { UnitNumber = "11006" },
      new() { UnitNumber = "11005" },
      new() { UnitNumber = "54777" },
      new() { UnitNumber = "110060" },
    ];

  [Fact]
  public void ExactNumberShowsOnlyThatTruck() =>
    Assert.Equal(
      "11006",
      Assert.Single(TruckMapSearch.Filter(Trucks, " 11006 ")).UnitNumber
    );

  [Fact]
  public void PartialNumberFiltersAndUnknownNumberShowsNone()
  {
    Assert.Equal(3, TruckMapSearch.Filter(Trucks, "1100").Count);
    Assert.Empty(TruckMapSearch.Filter(Trucks, "missing"));
  }

  [Fact]
  public void ClearingSearchRestoresAllTrucks() =>
    Assert.Equal(4, TruckMapSearch.Filter(Trucks, "").Count);

  [Fact]
  public void NumericSearchMatchesStartOfTruckOrTrailerNotDigitsInside()
  {
    var trucks = Trucks;
    trucks[0].TrailerNumber = "W5631";
    trucks[1].TrailerNumber = "9P1175";
    trucks[2].TrailerNumber = "44120";
    Assert.Equal(
      "54777",
      Assert.Single(TruckMapSearch.Filter(trucks, "5")).UnitNumber
    );
    Assert.Empty(TruckMapSearch.Filter(trucks, "777"));
    Assert.Equal(
      "54777",
      Assert.Single(TruckMapSearch.Filter(trucks, "44")).UnitNumber
    );
  }

  [Theory]
  [InlineData(" aidar ")]
  [InlineData("AUBAKIR")]
  [InlineData("w563")]
  public void FindsDriverAndTrailerIgnoringCase(string query)
  {
    var trucks = Trucks;
    trucks[0].DriverName = "Aidar Aubakir";
    trucks[0].TrailerNumber = "W5631";
    Assert.Same(trucks[0], Assert.Single(TruckMapSearch.Filter(trucks, query)));
  }

  [Fact]
  public void SharedDriverReturnsAllMatchesWithoutDuplicatingTrucks()
  {
    var trucks = Trucks;
    trucks[0].DriverName = trucks[1].DriverName = "James Campos";
    trucks[0].TrailerNumber = "James";
    Assert.Equal(2, TruckMapSearch.Filter(trucks, "James").Count);
    trucks[1].TrailerNumber = "11006";
    Assert.Same(
      trucks[0],
      Assert.Single(TruckMapSearch.Filter(trucks, "11006"))
    );
  }
}
