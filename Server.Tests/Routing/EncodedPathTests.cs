using System.Text.Json;
using API.Serialization;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Microsoft.AspNetCore.Http;

namespace Server.Tests.Routing;

[Trait("Category", "Routing")]
public class EncodedPathTests
{
  // The same points and the same string are pinned in the map's own tests
  // (Client/tests/fleetMap/encodedPath.test.js). The two codecs are written
  // in different languages and must never drift apart.
  internal static readonly RoutePoint[] Known =
  [
    new(38.5, -120.2),
    new(40.7, -120.95),
    new(43.252, -126.453),
    new(-33.868821, 151.209296),
    new(0.000001, -179.999999),
  ];
  internal const string KnownText =
    "_izlhA~rlgdF_{geC~ywl@_kwzCn`{nIhrabrCoddrpOk`er_A|clvvR";

  [Fact]
  public void EncodingIsTheAgreedStringAndReadsBackExactly()
  {
    Assert.Equal(KnownText, EncodedPath.Encode(Known));
    Assert.Equal(Known, EncodedPath.Decode(KnownText));
  }

  [Fact]
  public void SixDecimalPlacesSurviveAndNothingBelowThemMatters()
  {
    RoutePoint[] points =
    [
      new(35.1234564, -85.7654326),
      new(35.1234566, -85.7654324),
    ];
    var decoded = EncodedPath.Decode(EncodedPath.Encode(points));
    Assert.Equal(new RoutePoint(35.123456, -85.765433), decoded[0]);
    Assert.Equal(new RoutePoint(35.123457, -85.765432), decoded[1]);
  }

  [Theory]
  [InlineData("_izlhA")]
  [InlineData("_izlhA~rlgd")]
  [InlineData("_izl hA~rlgdF")]
  public void ADamagedStringIsRefusedRatherThanDrawnSomewhereElse(
    string text
  ) => Assert.Throws<FormatException>(() => EncodedPath.Decode(text));

  [Fact]
  public void NothingEncodesToNothing()
  {
    Assert.Equal("", EncodedPath.Encode([]));
    Assert.Empty(EncodedPath.Decode(""));
    Assert.Empty(EncodedPath.Decode(null));
  }

  [Theory]
  [InlineData("encoded", true)]
  [InlineData("ENCODED", true)]
  [InlineData(null, false)]
  [InlineData("something-else", false)]
  public void OnlyACallerThatAskedGetsTheEncodedForm(
    string? header,
    bool encoded
  )
  {
    var json = JsonSerializer.Serialize(
      new RouteLeg(12.5, 900, [.. Known]),
      Options(header)
    );
    using var document = JsonDocument.Parse(json);
    var root = document.RootElement;
    Assert.Equal(12.5, root.GetProperty("miles").GetDouble());
    Assert.Equal(900, root.GetProperty("seconds").GetDouble());
    if (encoded)
    {
      Assert.Equal(0, root.GetProperty("points").GetArrayLength());
      Assert.Equal(KnownText, root.GetProperty("path").GetString());
    }
    else
    {
      Assert.False(root.TryGetProperty("path", out _));
      Assert.Equal(Known.Length, root.GetProperty("points").GetArrayLength());
      Assert.Equal(
        38.5,
        root.GetProperty("points")[0].GetProperty("latitude").GetDouble()
      );
    }
  }

  [Theory]
  [InlineData("encoded", true)]
  [InlineData(null, false)]
  public void TheEmptyDriveBetweenLoadsFollowsTheSameRule(
    string? header,
    bool encoded
  )
  {
    var connection = new NextLoadConnection(42, [.. Known]);
    var json = JsonSerializer.Serialize(connection, Options(header));
    using var document = JsonDocument.Parse(json);
    Assert.Equal(42, document.RootElement.GetProperty("miles").GetDouble());
    Assert.Equal(
      encoded ? 0 : Known.Length,
      document.RootElement.GetProperty("points").GetArrayLength()
    );
    Assert.Equal(encoded, document.RootElement.TryGetProperty("path", out _));
    var read = JsonSerializer.Deserialize<NextLoadConnection>(
      json,
      Options(null)
    )!;
    Assert.Equal(42, read.Miles);
    Assert.Equal(Known, read.Points);
  }

  [Fact]
  public void ALegWithoutGeometryIsNeverGivenAnEmptyPath()
  {
    var json = JsonSerializer.Serialize(
      new RouteLeg(1, 2, []),
      Options("encoded")
    );
    Assert.DoesNotContain("path", json);
  }

  [Theory]
  [InlineData("encoded")]
  [InlineData(null)]
  public void EitherFormReadsBackAsTheSameLeg(string? header)
  {
    var leg = new RouteLeg(12.5, 900, [.. Known]);
    var read = JsonSerializer.Deserialize<RouteLeg>(
      JsonSerializer.Serialize(leg, Options(header)),
      Options(null)
    )!;
    Assert.Equal(leg.Miles, read.Miles);
    Assert.Equal(leg.Seconds, read.Seconds);
    Assert.Equal(leg.Points, read.Points);
  }

  [Fact]
  public void ADamagedPathInARequestIsABadRequestNotACrash() =>
    Assert.Throws<JsonException>(
      () =>
        JsonSerializer.Deserialize<RouteLeg>(
          """{"miles":1,"seconds":2,"points":[],"path":"_izlhA"}""",
          Options(null)
        )
    );

  [Fact]
  public void TheReferenceRoadIsCutToWhatLiesBehindTheTruck()
  {
    List<RouteLeg> reference =
    [
      new(10, 600, [new(35, -90), new(35, -89.5), new(35, -89)]),
      new(20, 1200, [new(35, -89), new(35, -88), new(35, -87), new(35, -86)]),
      new(30, 1800, [new(35, -86), new(35, -85)]),
    ];

    var head = DisplayRouteGeometry.TravelledHead(
      reference,
      new(35.001, -87.4)
    );

    Assert.Equal(2, head.Count);
    Assert.Same(reference[0], head[0]);
    // The matched segment is kept whole: the map searches the head again and
    // has to land on the same segment.
    Assert.Equal([new(35, -89), new(35, -88), new(35, -87)], head[1].Points);
  }

  [Fact]
  public void ATruckNowhereNearTheReferenceLeavesItWhole()
  {
    List<RouteLeg> reference =
    [
      new(10, 600, [new(35, -90), new(35, -89)]),
      new(20, 1200, [new(35, -89), new(35, -88)]),
    ];
    // Three miles north: past the two the map allows before it gives up and
    // draws the reference on its own.
    var head = DisplayRouteGeometry.TravelledHead(
      reference,
      new(35 + 3d / 69, -89.5)
    );
    Assert.Equal(reference, head);
  }

  [Fact]
  public void ARoadThatPassesTheTruckTwiceIsCutAtTheFirstPassLikeTheMapDoes()
  {
    List<RouteLeg> reference =
    [
      new(10, 600, [new(35, -90), new(35, -89), new(35, -88)]),
      new(10, 600, [new(35, -88), new(35, -89), new(35, -90)]),
    ];
    var head = DisplayRouteGeometry.TravelledHead(reference, new(35, -89.5));
    Assert.Single(head);
    Assert.Equal([new(35, -90), new(35, -89)], head[0].Points);
  }

  [Fact]
  public void TheFullAndTheGeometryFreeAnswersAgreeOnHowManyLegsThereAre()
  {
    // The client compares the two counts and asks again in full when they
    // differ. Disagreement would make it fetch the geometry on every poll.
    RoutePlan Plan() =>
      new()
      {
        Id = Guid.NewGuid(),
        Version = 1,
        FromCurrentPosition = true,
        Route = new()
        {
          Legs = [new(5, 300, [new(35, -87.4), new(35, -87), new(35, -86)])],
        },
        ReferenceRoute = new()
        {
          Legs =
          [
            new(10, 600, [new(35, -90), new(35, -89)]),
            new(20, 1200, [new(35, -89), new(35, -88), new(35, -87)]),
            new(30, 1800, [new(35, -87), new(35, -86)]),
          ],
        },
      };
    var full = Plan();
    PlanningReadService.TrimForDisplay(full);
    Assert.Equal(2, full.ReferenceRoute!.Legs.Count);

    PlanningReadService.TrimForDisplay(full, full.Id, full.Version);
    Assert.Equal(2, full.ReferenceRoute.Legs.Count);
    Assert.All(full.ReferenceRoute.Legs, leg => Assert.Empty(leg.Points));
  }

  [Fact]
  public void ARouteThatStartsAtAStopKeepsItsWholeReference()
  {
    var plan = new RoutePlan
    {
      FromCurrentPosition = false,
      Route = new() { Legs = [new(5, 300, [new(35, -89), new(35, -88)])] },
      ReferenceRoute = new()
      {
        Legs =
        [
          new(10, 600, [new(35, -90), new(35, -89)]),
          new(20, 1200, [new(35, -89), new(35, -88)]),
        ],
      },
    };
    PlanningReadService.TrimForDisplay(plan);
    Assert.Equal(2, plan.ReferenceRoute.Legs.Count);
  }

  private static JsonSerializerOptions Options(string? header)
  {
    var context = new DefaultHttpContext();
    if (header is not null)
      context.Request.Headers[RouteLegJsonConverter.Header] = header;
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var http = new HttpContextAccessor { HttpContext = context };
    options.Converters.Add(new RouteLegJsonConverter(http));
    options.Converters.Add(new NextLoadConnectionJsonConverter(http));
    return options;
  }
}
