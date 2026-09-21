using Application.Features.Routing.Services.Routes;
using Application.Models;
using Domain.Models.Routing;
using DispatchEntity = Domain.Entities.Dispatch.Dispatch;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class RouteCoordinateIdentityTests
{
  [Fact]
  public void EquivalentCoordinateScalesHaveOneRoadIdentity()
  {
    var load = new DispatchEntity
    {
      Stops =
      [
        new()
        {
          Sequence = 1,
          Latitude = 35m,
          Longitude = -80m,
        },
        new()
        {
          Sequence = 2,
          Latitude = 36m,
          Longitude = -81m,
        },
      ],
    };
    var profile = new TruckRouteProfile();
    var expected = BaseRouteService.Signature(load, profile);
    load.Stops[0].Latitude = 35.000000m;
    load.Stops[0].Longitude = -80.00m;
    Assert.Equal(expected, BaseRouteService.Signature(load, profile));
    load.Stops[0].Latitude = 35.000001m;
    Assert.NotEqual(expected, BaseRouteService.Signature(load, profile));
  }

  [Fact]
  public void CanonicalDecimalsKeepTheEntireRepresentableValue()
  {
    decimal?[] values =
    [
      null,
      0m,
      -0.0000m,
      1.230000m,
      -0.0000000000000000000000000001m,
      decimal.MaxValue,
      decimal.MinValue,
    ];
    Assert.All(
      values,
      value => Assert.Equal(value, DecimalValue.Normalize(value))
    );
    Assert.Equal(
      decimal.GetBits(35m),
      decimal.GetBits(DecimalValue.Normalize(35.000000m)!.Value)
    );
  }
}
