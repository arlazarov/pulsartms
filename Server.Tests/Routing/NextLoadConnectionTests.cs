using System.Text.Json;
using Application.Features.Routing.Models;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
public sealed class NextLoadConnectionTests
{
  [Fact]
  public void MapConnectionUsesOneGeometryWithoutFullRouteMetadata()
  {
    var points = new List<RoutePoint> { new(40, -80), new(41, -79) };
    var route = new TruckRoute
    {
      Miles = 161,
      Points = points,
      Legs = [new(161, 3600, points)],
    };
    var connection = NextLoadConnection.From(route)!;
    Assert.Same(points, connection.Points);
    Assert.Equal(161, connection.Miles);
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(connection));
    Assert.Equal(2, json.RootElement.EnumerateObject().Count());
    route.Points = [];
    Assert.Equal(points, NextLoadConnection.From(route)!.Points);
  }

  [Fact]
  public void InvalidConnectionsAreNotSentToTheMap()
  {
    Assert.Null(NextLoadConnection.From(null));
    Assert.Null(NextLoadConnection.From(new() { Miles = 10 }));
    Assert.Null(
      NextLoadConnection.From(
        new() { Miles = double.NaN, Points = [new(40, -80), new(41, -79)] }
      )
    );
    Assert.Null(
      NextLoadConnection.From(
        new() { Miles = 10, Points = [new(40, -80), new(91, -79)] }
      )
    );
  }
}
