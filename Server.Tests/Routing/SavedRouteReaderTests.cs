using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class SavedRouteReaderTests
{
  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("{")]
  [InlineData("null")]
  [InlineData("{}")]
  [InlineData("{\"legs\":null}")]
  [InlineData("{\"legs\":[null]}")]
  [InlineData("{\"legs\":[{\"points\":null}]}")]
  [InlineData("{\"legs\":[{\"points\":[null,null]}]}")]
  public void MissingCorruptOrIncompleteGeometryIsACacheMiss(string? json) =>
    Assert.Null(SavedRouteReader.Route(json, 1));

  [Fact]
  public void CompleteGeometryIsValidatedForItsExpectedLegCount()
  {
    var route = new TruckRoute
    {
      Miles = 10,
      Seconds = 100,
      Legs = [new(10, 100, [new(40, -80), new(41, -79)])],
    };
    var json = JsonSerializer.Serialize(route, RoutingJson.Options);
    Assert.NotNull(SavedRouteReader.Route(json, 1));
    Assert.Null(SavedRouteReader.Route(json, 2));
    route.Legs[0].Points[0] = new(91, -80);
    Assert.Null(
      SavedRouteReader.Route(
        JsonSerializer.Serialize(route, RoutingJson.Options),
        1
      )
    );
    Assert.Null(SavedRouteReader.Plan("{"));
  }
}
