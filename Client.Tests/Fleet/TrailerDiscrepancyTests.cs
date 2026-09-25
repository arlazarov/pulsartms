using Client.Models.DTO.Fleet;
using Client.Pages.FleetMap;

namespace Client.Tests.Fleet;

// A trailer conflict names both sources and chooses neither: 11005 on
// September 25 read "— or 055904 ?", 11006 "44120 or 9P1175?".
[Trait("Category", "Fleet")]
[Trait("Kind", "Unit")]
public sealed class TrailerDiscrepancyTests
{
  [Theory]
  [InlineData(
    "telemetry",
    "",
    "055904",
    "Telemetry: none · Work: 055904",
    "telemetry reports no trailer; the truck's current work names 055904"
  )]
  [InlineData(
    "telemetry",
    "44120",
    "9P1175",
    "Telemetry: 44120 · Work: 9P1175",
    "telemetry reports 44120; the truck's current work names 9P1175"
  )]
  [InlineData(
    null,
    "",
    "77001",
    "77001: also named for another truck",
    "neither is confirmed, so it is not assigned to either"
  )]
  public void BothSourcesAreNamedAndNeitherIsChosen(
    string? source,
    string trailer,
    string conflict,
    string text,
    string explanation
  )
  {
    var shown = FleetMap.TrailerDiscrepancy(
      new TruckLocationMapDto
      {
        TrailerSource = source,
        TrailerNumber = trailer,
        TrailerConflictNumber = conflict,
      }
    );

    Assert.NotNull(shown);
    Assert.Equal(text, shown.Value.Text);
    Assert.Contains(explanation, shown.Value.Title);
    Assert.DoesNotContain("?", shown.Value.Text);
  }

  [Fact]
  public void NoConflictShowsNoDiscrepancy() =>
    Assert.Null(
      FleetMap.TrailerDiscrepancy(
        new TruckLocationMapDto
        {
          TrailerSource = "telemetry",
          TrailerNumber = "44120",
        }
      )
    );
}
