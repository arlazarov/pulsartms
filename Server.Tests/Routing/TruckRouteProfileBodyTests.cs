using System.Text.Json;
using Application.Features.Routing.Models;
using Domain.Models.Routing;

namespace Server.Tests.Routing;

// The profile endpoint now takes an Application contract instead of the
// Domain profile (AGENTS.md: the web layer names Application alone). On
// the wire it is the same body: read with the web serializer options, a
// full, partial, empty or differently cased profile gives the same
// profile, missing fields taking the profile's own defaults, and it
// writes back unchanged.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class TruckRouteProfileBodyTests
{
  private static readonly JsonSerializerOptions Web = new(
    JsonSerializerDefaults.Web
  );

  [Theory]
  [InlineData(
    """
      {"trailerLengthFeet":48,"heightFeet":13.6,"widthFeet":8.5,
       "lengthFeet":70,"weightPounds":78000,"axles":5,
       "axleWeightPounds":19000,"hazmat":"USHazmatClass3","confirmed":true,
       "usesFleetDefaults":false,"tankGallons":240,"mpg":6.5,
       "reserveGallons":30,"fillPercent":95,"stopCostUsd":25,
       "driverHourlyCostUsd":40,"maxDetourMinutes":12,"cadToUsd":0.73,
       "useIfta":false}
      """
  )]
  [InlineData("""{"heightFeet":13.1,"confirmed":true}""")]
  [InlineData("{}")]
  [InlineData("""{"HeightFeet":"12.5","MPG":7,"unknown":1}""")]
  public void TheBodyReadsAndWritesAsTheProfile(string json)
  {
    var body = JsonSerializer.Deserialize<TruckRouteProfileBody>(json, Web)!;
    var profile = JsonSerializer.Deserialize<TruckRouteProfile>(json, Web)!;

    Assert.Equal(
      JsonSerializer.Serialize(profile, Web),
      JsonSerializer.Serialize(body.Value, Web)
    );
    Assert.Equal(
      JsonSerializer.Serialize(profile, Web),
      JsonSerializer.Serialize(body, Web)
    );
  }

  [Fact]
  public void AMissingBodyIsNoBody() =>
    Assert.Null(JsonSerializer.Deserialize<TruckRouteProfileBody>("null", Web));
}
