using Bunit;
using Client.Models.DTO.Messaging;
using Client.Models.DTO.Planning;
using Client.Pages.Messages;
using Client.Shared.DriverStatus.DriverDutySummary;
using Client.Shared.DriverStatus.DriverHours;
using Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Client.Tests.Fleet;

// The driver's clocks and rest as the map card, Dispatch and a conversation
// read them. The break clock is a US rule: it goes only when the server
// says the hours are read under Canadian rules, and stays when it says US
// or says nothing. The rest line keeps the status's own time apart from
// the rest built up across off duty, personal conveyance and sleeper.
[Trait("Category", "Fleet")]
[Trait("Kind", "Component")]
public sealed class DriverRulesDisplayTests
{
  private static readonly DriverHosClocks Clocks = new()
  {
    BreakMs = 8 * 3600000L,
    DriveMs = 26 * 60000L,
    ShiftMs = 26 * 60000L,
    CycleMs = 19 * 3600000L,
    UpdatedAt = DateTime.UtcNow,
    CurrentDutyStatus = "sleeperBerth",
  };

  private static string[] Labels(IRenderedComponent<DriverHours> hours) =>
    hours
      .FindAll(".driver-hours__label")
      .Select(label => label.TextContent)
      .ToArray();

  [Theory]
  [InlineData("CA", false)]
  [InlineData("US", true)]
  [InlineData(null, true)]
  public void TheBreakClockFollowsTheServersRuleset(
    string? rules,
    bool breakShown
  )
  {
    using var context = new BunitContext();
    var direct = context.Render<DriverHours>(p =>
      p.Add(x => x.Clocks, Clocks)
        .Add(x => x.Dials, false)
        .Add(x => x.ShowDutyStatus, false)
        .Add(x => x.Jurisdiction, rules)
    );
    // Dispatch passes the forecast's duty status instead of a word.
    var fromStatus = context.Render<DriverHours>(p =>
      p.Add(x => x.Clocks, Clocks)
        .Add(x => x.ShowDutyStatus, false)
        .Add(
          x => x.Status,
          new DriverDutyStatus(
            "sleeperBerth",
            null,
            null,
            DateTimeOffset.UtcNow
          )
          {
            Jurisdiction = rules,
          }
        )
    );
    foreach (var hours in new[] { direct, fromStatus })
    {
      Assert.Equal(breakShown, Labels(hours).Contains("Break"));
      Assert.Contains("Drive", Labels(hours));
      Assert.Contains("Cycle", Labels(hours));
    }
  }

  [Theory]
  [InlineData("CA", false)]
  [InlineData("US", true)]
  [InlineData(null, true)]
  public void AConversationReadsTheRulesetItsContextCarries(
    string? rules,
    bool breakShown
  )
  {
    using var context = new ClientComponentContext(
      (_, _) => throw new InvalidOperationException("No request expected.")
    );
    context.JSInterop.Mode = JSRuntimeMode.Loose;
    var panel = context.Render<ConversationContextPanel>(p =>
      p.Add(x => x.ConversationId, Guid.NewGuid())
        .Add(
          x => x.Context,
          new ConversationContext(
            Guid.NewGuid(),
            "Driver",
            "one-truck",
            [],
            [],
            new ContextHours(
              true,
              Clocks.BreakMs,
              Clocks.DriveMs,
              Clocks.ShiftMs,
              Clocks.CycleMs,
              DateTime.UtcNow,
              "driving"
            ),
            new ContextDuty("driving", null) { Jurisdiction = rules }
          )
        )
    );
    var labels = panel
      .FindAll(".driver-hours__label")
      .Select(label => label.TextContent)
      .ToArray();
    Assert.Equal(breakShown, labels.Contains("Break"));
    Assert.Contains("Drive", labels);
  }

  // Sleeper for 3h 30m after off duty and personal conveyance: one rest
  // block of 5h, so the status says 3h 30m and the rest 5h 00m to go.
  [Fact]
  public void TheRestLineKeepsTheStatusAndTheRestBlockApart()
  {
    using var context = new BunitContext();
    var now = DateTimeOffset.UtcNow;
    var row = context.Render<DriverDutySummary>(p =>
      p.Add(x => x.Reading, "row")
        .Add(x => x.CurrentStatus, "sleeperBerth")
        .Add(x => x.ClockUpdatedAt, now.UtcDateTime)
        .Add(
          x => x.Status,
          new DriverDutyStatus(
            "sleeperBerth",
            now.AddMinutes(-210),
            now.AddMinutes(-300),
            now
          )
          {
            CycleResetHours = 34,
            CycleResetCountry = "US",
            CycleResetRemainingMinutes = 34 * 60 - 300,
            DailyRestRemainingMinutes = 300,
          }
        )
    );
    var text = row.Find(".driver-duty--row").TextContent;
    Assert.Contains("Sleeper", text);
    Assert.Contains("3h 30m", text);
    Assert.Contains("10h rest in 5h 00m", text);
    Assert.Contains("34h reset in 29h 00m", text);
    Assert.DoesNotContain("US", text);
  }

  // Clocks that now name another status, or a reading older than the
  // summary trusts, keep the status and say no duration at all.
  [Fact]
  public void AStatusTheClocksNoLongerShowSaysNoDuration()
  {
    using var context = new BunitContext();
    var now = DateTimeOffset.UtcNow;
    var status = new DriverDutyStatus(
      "sleeperBerth",
      now.AddMinutes(-210),
      now.AddMinutes(-300),
      now
    );
    var changed = context.Render<DriverDutySummary>(p =>
      p.Add(x => x.Reading, "row")
        .Add(x => x.CurrentStatus, "driving")
        .Add(x => x.ClockUpdatedAt, now.UtcDateTime)
        .Add(x => x.Status, status)
    );
    var stale = context.Render<DriverDutySummary>(p =>
      p.Add(x => x.Reading, "row")
        .Add(x => x.CurrentStatus, "sleeperBerth")
        .Add(x => x.ClockUpdatedAt, now.UtcDateTime.AddMinutes(-10))
        .Add(x => x.Status, status)
    );
    Assert.Equal(
      "Driving",
      changed.Find(".driver-duty__status strong").TextContent
    );
    Assert.Empty(changed.FindAll(".driver-duty__rest"));
    Assert.DoesNotContain("3h 30m", changed.Markup);
    Assert.Empty(stale.FindAll(".driver-duty__rest"));
    Assert.DoesNotContain("3h 30m", stale.Markup);
  }
}
