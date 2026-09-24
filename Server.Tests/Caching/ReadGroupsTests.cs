using Application.Caching;

namespace Server.Tests.Caching;

// Group names travel to other instances as CacheInvalidation rows: an
// instance running the previous build must still recognise every one.
[Trait("Category", "Caching")]
[Trait("Kind", "Unit")]
public sealed class ReadGroupsTests
{
  [Fact]
  public void GroupNamesStayWhatOtherInstancesExpect()
  {
    Assert.Equal(
      [
        "dispatch",
        "board",
        "execution",
        "route-previews",
        "fuel",
        "fleet-catalog",
        "settings",
        "driver-groups",
      ],
      new[]
      {
        ReadGroups.Dispatch,
        ReadGroups.Board,
        ReadGroups.Execution,
        ReadGroups.RoutePreviews,
        ReadGroups.Fuel,
        ReadGroups.FleetCatalog,
        ReadGroups.Settings,
        ReadGroups.DriverGroups,
      }
    );
    Assert.Equal(
      ["dispatch", "board", "execution", "route-previews"],
      ReadGroups.Work
    );
  }
}
