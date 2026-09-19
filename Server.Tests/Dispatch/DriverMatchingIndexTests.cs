using Application.Features.Dispatch.Commands.SyncDispatche;
using Domain.Entities.Fleet;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DriverMatchingIndexTests
{
  [Fact]
  public void IndexedMatchingPreservesTokenNormalizationAndAmbiguity()
  {
    var drivers = new[]
    {
      new Driver { Name = "Anne Marie" },
      new Driver { Name = "Anne" },
      new Driver { Name = "John-Paul Smith" },
    };
    var index = new DriverMatcher.Index(drivers);
    foreach (
      var name in new[]
      {
        "",
        "ANNE MARIE",
        "Smith, John Paul",
        "Unknown",
        "John Paul Smith and Driver",
      }
    )
    {
      Assert.Same(DriverMatcher.Match(drivers, name), index.Match(name));
      Assert.Same(DriverMatcher.Match(drivers, name), index.Match(name));
    }
    Assert.Null(index.Match("Anne Marie"));
    Assert.Same(drivers[2], index.Match("Smith, John Paul"));
  }
}
