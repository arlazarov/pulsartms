using Application.Diagnostics;
using Domain.Rules;

namespace Server.Tests.Eta;

// A forecast published to readers and then not saved is taken back. Truck
// 11006's was, every few minutes (September 26), and the line said only
// that: the reason it was not saved is now counted and logged with it.
public sealed partial class EtaChainInputTests
{
  [Fact]
  public async Task AForecastTakenBackSaysWhyItWasNotSaved()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    f.Publication.BeforeBegin = () =>
      throw RoutePlanningException.InputsBusy(DateTime.UtcNow.AddSeconds(1));
    var busy = Counted("unsaved-planning-busy");
    var removed = Counted("unsaved-removed");

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.True(Counted("unsaved-removed") > removed);
    Assert.True(Counted("unsaved-planning-busy") > busy);

    // Nothing in the way: saved, kept, and nothing taken back.
    f.Publication.BeforeBegin = null;
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
  }

  // Other tests count too, in parallel: only increases are asserted.
  private static long Counted(string stage) =>
    PerformanceStages.Snapshot().GetValueOrDefault($"eta-memory/{stage}")?.Items
    ?? 0;
}
