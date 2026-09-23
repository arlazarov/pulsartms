using Application.Features.Routing.Commands;
using Domain.Models.Routing;
using Domain.Rules.Routing;

namespace Server.Tests.Fuel;

// What moves a prepared plan, what may send it, and what refuses a stale
// confirmation.
[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelIssuePolicyTests
{
  // A price moving a purchase by less than an extra stop must save leaves
  // the stations where they are and moves only the estimate.
  [Theory]
  [InlineData(5.645, 5.700, 150, false)]
  [InlineData(5.645, 5.711, 150, false)]
  [InlineData(5.645, 5.712, 150, true)]
  [InlineData(5.645, 5.600, 250, true)]
  [InlineData(5.645, 5.646, 1000, false)]
  public void OnlyAPriceThatCouldMoveAStopIsAReasonToSearch(
    double saved,
    double today,
    double gallons,
    bool material
  )
  {
    var stop = Stop(saved, gallons);
    Assert.Equal(
      material,
      FuelPriceMateriality.Material([stop], _ => Quote(today))
    );
  }

  [Fact]
  public void AStationNoLongerPricedIsAlwaysAReasonToSearch()
  {
    Assert.True(FuelPriceMateriality.Material([Stop(5.6, 10)], _ => null));
  }

  [Fact]
  public void ASmallChangeMovesTheEstimateAndKeepsTheStations()
  {
    var stop = Stop(5.645, 100);
    var plan = new FuelPlan
    {
      Stops = [stop],
      PurchaseCostUsd = 564.5,
      EconomicCostUsd = 570,
    };
    FuelPriceMateriality.Reprice(plan, _ => Quote(5.695));
    Assert.Equal(5.695, stop.EconomicUsdPerGallon, 9);
    Assert.Equal(569.5, plan.PurchaseCostUsd, 6);
    Assert.Equal(575, plan.EconomicCostUsd, 6);
    Assert.Single(plan.Stops);
  }

  [Fact]
  public void SendingIsManualUnlessACompanyTurnsItOn()
  {
    var plan = Ready();
    Assert.Equal(
      "Automatic fuel plan sending is off.",
      FuelAutoSend.Refusal(enabled: false, channelReady: true, plan)
    );
    Assert.Null(FuelAutoSend.Refusal(enabled: true, channelReady: true, plan));
  }

  [Fact]
  public void AutomaticSendingNeedsAChannelADriverOnDutyAndSomethingNew()
  {
    Assert.Equal(
      "No sending channel is configured.",
      FuelAutoSend.Refusal(true, false, Ready())
    );
    var resting = Ready();
    resting.IssueState = FuelIssueStates.AwaitingDuty;
    Assert.Equal(
      "The driver is not on duty.",
      FuelAutoSend.Refusal(true, true, resting)
    );
    var unknown = Ready();
    unknown.IssueState = FuelIssueStates.HosUnknown;
    Assert.Equal(
      "The driver's hours are unknown.",
      FuelAutoSend.Refusal(true, true, unknown)
    );
    var critical = Ready();
    critical.IssueCritical = true;
    Assert.Equal(
      "A planned stop is out of reach.",
      FuelAutoSend.Refusal(true, true, critical)
    );
    var sent = Ready();
    sent.Stops[0].Sent = new(DateTime.UtcNow, "u", "manual", false);
    Assert.Equal(
      "This plan was already sent.",
      FuelAutoSend.Refusal(true, true, sent)
    );
    // Changed since it was sent: the driver has the old one.
    sent.Stops[0].Sent = sent.Stops[0].Sent! with { Changed = true };
    Assert.Null(FuelAutoSend.Refusal(true, true, sent));
    var provisional = Ready();
    provisional.Stops[0].IssueHorizon = FuelIssueHorizons.Upcoming;
    Assert.Equal(
      "No fuel stop is planned for this shift.",
      FuelAutoSend.Refusal(true, true, provisional)
    );
  }

  // Asked again at the moment of sending: a setting turned off while the
  // send waited stops it, whatever was decided before.
  [Fact]
  public void TurningItOffStopsASendThatWasWaiting()
  {
    var plan = Ready();
    Assert.Null(FuelAutoSend.Refusal(true, true, plan));
    Assert.NotNull(FuelAutoSend.Refusal(false, true, plan));
  }

  [Fact]
  public void AConfirmationForAPlanThatMovedIsRefused()
  {
    var at = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);
    var leg = Guid.NewGuid();
    var saved = new TruckFuelPlanSnapshot(
      Guid.NewGuid(),
      Guid.NewGuid(),
      at,
      new FuelPlan(),
      [],
      null
    )
    {
      RootExecutionLegId = leg,
      AssignmentRevision = 4,
    };
    var keys = new HashSet<string> { "a", "b" };
    FuelIssueSentRequest Request(DateTime when, params string[] visits) =>
      new(when, visits) { ExecutionLegId = leg, AssignmentRevision = 4 };

    Assert.False(ConfirmFuelIssueSentHandler.Moved(saved, Request(at, "a"), keys));
    Assert.True(
      ConfirmFuelIssueSentHandler.Moved(saved, Request(at.AddSeconds(1), "a"), keys)
    );
    Assert.True(ConfirmFuelIssueSentHandler.Moved(saved, Request(at, "c"), keys));
    Assert.True(
      ConfirmFuelIssueSentHandler.Moved(
        saved,
        Request(at, "a") with { AssignmentRevision = 5 },
        keys
      )
    );
    Assert.True(
      ConfirmFuelIssueSentHandler.Moved(
        saved,
        Request(at, "a") with { ExecutionLegId = Guid.NewGuid() },
        keys
      )
    );
  }

  private static FuelPlan Ready() =>
    new()
    {
      IssueState = FuelIssueStates.Ready,
      Stops =
      [
        new()
        {
          StationId = Guid.NewGuid(),
          IssueHorizon = FuelIssueHorizons.Current,
        },
      ],
    };

  private static FuelPlanStop Stop(double price, double gallons) =>
    new()
    {
      StationId = Guid.NewGuid(),
      EconomicUsdPerGallon = price,
      CashUsdPerGallon = price,
      BuyGallons = gallons,
    };

  private static FuelPriceMateriality.Quote Quote(double price) =>
    new(price, price, price, price);
}
