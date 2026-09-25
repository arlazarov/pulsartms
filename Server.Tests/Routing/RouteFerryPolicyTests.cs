using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Server.Tests.Support;

namespace Server.Tests.Routing;

// AMF1414's route options on September 25 included one across Lake
// Michigan by the Ludington-Manitowoc ferry (66 points mid-lake, through
// both ports), although roads around the lake existed. Routes are now
// asked for without ferries and their ferry sections are read: a ferry
// road is offered only when no road-only road exists, labelled, and is
// never taken without the dispatcher choosing it.
[Trait("Category", "Routing")]
[Trait("Kind", "Integration")]
public sealed class RouteFerryPolicyTests
{
  private static readonly RoutePoint Ticonderoga = new(43.8486707, -73.4234531);
  private static readonly RoutePoint DePere = new(44.4488805, -88.0603806);
  private static readonly RoutePoint[] AroundTheLake =
  [
    new(39.7684, -86.1581),
    new(41.8781, -87.6298),
  ];
  private static readonly RoutePoint[] ByFerry =
  [
    new(39.7684, -86.1581),
    new(43.955, -86.452),
    new(44.089, -87.657),
  ];

  [Fact]
  public async Task RoadOnlyOptionsAreOfferedWithoutTheFerry()
  {
    await using var fixture = await TomTomRouteFixture.CreateAsync();
    fixture.Handler.Detours = [ByFerry, AroundTheLake];
    fixture.Handler.Ferries.Add(0);

    var roads = await fixture.Provider.CalculateAlternativesAsync(
      [Ticonderoga, DePere],
      Profile(),
      default
    );

    var road = Assert.Single(roads);
    Assert.False(road.Ferry);
    Assert.Equal(AroundTheLake[1], road.Legs[0].Points[2]);
    var query = Assert.Single(fixture.Handler.Queries);
    Assert.Contains("avoid=ferries", query);
    Assert.Contains("sectionType=ferry", query);
  }

  // With no road-only road, the ferry roads are the fallback: offered,
  // flagged and warned about, for the dispatcher to choose or not. The
  // cached answer keeps the flag.
  [Fact]
  public async Task WithoutARoadOnlyRouteTheFerryIsOfferedLabelled()
  {
    await using var fixture = await TomTomRouteFixture.CreateAsync();
    fixture.Handler.Detours = [ByFerry, [.. ByFerry, AroundTheLake[1]]];
    fixture.Handler.Ferries.UnionWith([0, 1]);

    for (var attempt = 0; attempt < 2; attempt++)
    {
      var roads = await fixture.Provider.CalculateAlternativesAsync(
        [Ticonderoga, DePere],
        Profile(),
        default
      );

      Assert.Equal(2, roads.Count);
      Assert.All(
        roads,
        road =>
        {
          Assert.True(road.Ferry);
          Assert.Contains(RouteSectionValidator.FerryWarning, road.Warnings);
        }
      );
    }
    Assert.Single(fixture.Handler.Queries);
  }

  // A road bought on its own (base roads, plans, approaches) is taken
  // without anyone choosing it, so one that crosses by ferry is refused
  // and planning says where to choose it.
  [Fact]
  public async Task AnAutomaticRouteNeverTakesAFerry()
  {
    await using var fixture = await TomTomRouteFixture.CreateAsync();
    fixture.Handler.Detours = [ByFerry];
    fixture.Handler.Ferries.Add(0);

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () =>
        fixture.Provider.CalculateAsync(
          [Ticonderoga, DePere],
          Profile(),
          default
        )
    );

    Assert.Contains("crosses by ferry", error.Message);
    Assert.Contains("avoid=ferries", Assert.Single(fixture.Handler.Queries));
    fixture.Handler.Detours = [AroundTheLake];
    fixture.Handler.Ferries.Clear();
    var road = await fixture.Provider.CalculateAsync(
      [Ticonderoga, new(43.9, -73.4), DePere],
      Profile(),
      default
    );
    Assert.False(road.Ferry);
  }

  private static TruckRouteProfile Profile() =>
    new()
    {
      Confirmed = true,
      HeightFeet = 13.5,
      WidthFeet = 8.5,
      LengthFeet = 70,
      WeightPounds = 80000,
      Axles = 5,
      AxleWeightPounds = 17000,
    };
}
