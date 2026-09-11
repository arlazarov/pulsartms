using Application.Features.Eta.Algorithms;
using Application.Features.Eta.Models;
using Server.Tests.Support;

namespace Server.Tests.Eta;

[Trait("Category", "Eta")]
[Trait("Kind", "Unit")]
public sealed class HosBorderCycleTests
{
  private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

  [Theory]
  [InlineData("CA", "US")]
  [InlineData("US", "CA")]
  public void ReconciledJurisdictionsPreserveTheAnchorAndProjectedDriving(string origin, string destination)
  {
    var history = History();
    var ledger = new HosCycleFeasibility(Now, HosForecastFixture.Clocks(Now, history), origin, history);
    var starting = ledger.BalanceMinutes(Now);
    ledger.Observe(Now, Now.AddHours(2), "driving");
    ledger.Enter(destination);
    Assert.True(ledger.Verified);
    Assert.True(ledger.RecapVerified);
    Assert.Equal(starting - 120, ledger.BalanceMinutes(Now.AddHours(2)));
    ledger.Enter(origin);
    Assert.Equal(starting - 120, ledger.BalanceMinutes(Now.AddHours(2)));
  }

  [Fact]
  public void DifferentHistoricalWindowsDoNotTransferTheOtherJurisdictionsHours()
  {
    var history = HosForecastFixture.History(Now, cycleHours: 20, firstDayHours: 3) with { CanadaCycle = new(7, 70, 36) };
    var ledger = new HosCycleFeasibility(Now, HosForecastFixture.Clocks(Now, history), "US", history);
    ledger.Enter("CA");
    Assert.False(ledger.Verified);
    Assert.Null(ledger.BalanceMinutes(Now));
    ledger.Enter("US");
    Assert.False(ledger.Verified);
  }

  [Fact]
  public void BorderKeepsEarlierDrivingShortage()
  {
    var history = History();
    var ledger = new HosCycleFeasibility(Now, HosForecastFixture.Clocks(Now, history), "CA", history);
    ledger.Observe(Now, Now.AddHours(40), "driving");
    var shortage = ledger.DrivingShortfallMinutes;
    ledger.Enter("US");
    Assert.True(ledger.Verified);
    Assert.NotNull(ledger.FirstShortageAt);
    Assert.Equal(shortage, ledger.DrivingShortfallMinutes);
  }

  private static HosHistory History() => HosForecastFixture.History(Now, cycleHours: 20)
    with { CanadaCycle = new(7, 70, 36) };
}
