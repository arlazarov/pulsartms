using System.Text.Json;
using Application.Features.Dispatch.Queries;
using Server.Tests.Support;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchTelemetryTests
{
  [Fact]
  public async Task OnlyRequestedTruckStatusLeavesTheHandlerWithoutPlaybackOrLocationData()
  {
    var first = Guid.NewGuid();
    var other = Guid.NewGuid();
    var sender = new DispatchTelemetrySender(
      new()
      {
        Trucks =
        [
          new()
          {
            TruckId = first,
            Speed = 42,
            EngineState = "On",
            TrailerNumber = "T1",
            Latitude = 40,
            DriverName = "Private",
          },
          new() { TruckId = other, Speed = 12 },
        ],
        Points = [new() { TruckId = first, Latitude = 41 }],
      }
    );
    var result = await new GetDispatchTelemetryHandler(sender).Handle(
      new([first, first]),
      default
    );
    Assert.Equal(new(first, 42, "On", "T1"), Assert.Single(result.Response!));
    Assert.Equal(1, sender.Calls);
    var json = JsonSerializer.Serialize(result.Response);
    Assert.DoesNotContain("Latitude", json);
    Assert.DoesNotContain("Points", json);
    Assert.DoesNotContain("DriverName", json);
    Assert.DoesNotContain(other.ToString(), json);
    Assert.Empty(
      (
        await new GetDispatchTelemetryHandler(sender).Handle(new([]), default)
      ).Response!
    );
    Assert.Equal(1, sender.Calls);
  }

  [Fact]
  public void PageBoundsAndEmptyIdentitiesAreValidated()
  {
    Assert.Empty(
      new GetDispatchTelemetryQuery(
        Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray()
      ).Wrong()
    );
    Assert.NotEmpty(
      new GetDispatchTelemetryQuery(
        Enumerable.Range(0, 13).Select(_ => Guid.NewGuid()).ToArray()
      ).Wrong()
    );
    Assert.NotEmpty(new GetDispatchTelemetryQuery([Guid.Empty]).Wrong());
    Assert.NotEmpty(new GetDispatchPlanningSummariesQuery(Page: 0).Wrong());
  }
}
