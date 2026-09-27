using System.Text.Json;
using Client.Models.DTO;
using Client.Models.DTO.Planning;
using Client.Services;
using Client.Shared;

namespace Client.Tests.Routing;

// Stage 4e: a summary prepared with the driver's earlier duty arrives
// marked stale by its duty and says so in its message. The Client keeps
// both: the mark is part of the result, so a copy that differs only in it
// is another result, and the message is shown with a valid route.
[Trait("Category", "Routing")]
[Trait("Kind", "Unit")]
public sealed class PlanningStaleDependencyTests
{
  private const string DutyChanged =
    "Driver duty changed. The fuel hand-over is updating.";

  [Fact]
  public void TheDutyMarkAndItsMessageArriveAndAreShown()
  {
    var read = Read(
      $$"""
      "message": "{{DutyChanged}}",
      "isRefreshing": true,
      "staleDependencies": ["duty"],
      """
    );

    Assert.Equal(["duty"], read.StaleDependencies);
    Assert.NotEqual(read, read with { StaleDependencies = [] });
    Assert.Equal(DutyChanged, RouteMessageDisplay.For(read.Message, true));
  }

  // A server released before the mark sends none: read as no stale
  // dependency, equal to an empty one, and never a failed comparison.
  [Fact]
  public void AResultWithoutTheMarkReadsAsNone()
  {
    var read = Read("");

    Assert.Null(read.StaleDependencies);
    Assert.Equal(read, read with { StaleDependencies = [] });
  }

  private static AutomaticPlanningResult Read(string fields) =>
    JsonSerializer
      .Deserialize(
        $$"""
        {
          "success": true,
          "response": {
            {{fields}}
            "truckId": "{{Guid.NewGuid()}}",
            "notices": [],
            "workConflicts": []
          }
        }
        """,
        PlanningJsonContext.Default.RequestResponseDTOAutomaticPlanningResult
      )!
      .Response!;
}
