using Application.Diagnostics;
using Domain.Rules;

namespace Server.Tests.Eta;

// Readers see a forecast only once it is saved. Truck 11006's (September
// 26) was published before its save and taken back when the save failed,
// so the map's ETA alternated between shown and nothing, and the forecast
// already saved and shown was taken back with it.
public sealed partial class EtaChainInputTests
{
  [Fact]
  public async Task AFailedSaveNeverPublishesAndSaysWhy()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    f.Publication.BeforeBegin = () =>
      throw RoutePlanningException.InputsBusy(DateTime.UtcNow.AddSeconds(1));
    var busy = Counted("unsaved-planning-busy");

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.True(Counted("unsaved-planning-busy") > busy);

    // Nothing in the way: saved, then published.
    f.Publication.BeforeBegin = null;
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
  }

  // A saved forecast is shown; the next refresh calculates anew and cannot
  // save. Readers keep the saved one: the new result is not published and
  // the saved one is not taken back.
  [Fact]
  public async Task AFailedSaveLeavesTheSavedForecastShown()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var memory = f.Services.EtaMemory;
    // Another signature makes the next refresh calculate instead of
    // reusing this result.
    var shown = memory.Results[f.Current.Id] with
    {
      Signature = "earlier",
    };
    memory.Results[f.Current.Id] = shown;
    f.Publication.BeforeBegin = () =>
      throw RoutePlanningException.InputsBusy(DateTime.UtcNow.AddSeconds(1));

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Same(shown, memory.Results[f.Current.Id]);

    f.Publication.BeforeBegin = null;
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    Assert.NotSame(shown, memory.Results[f.Current.Id]);
    Assert.True(
      memory.Results[f.Current.Id].Value.CalculatedAt
        >= shown.Value.CalculatedAt
    );
  }

  // A refresh that fails because a check demonstrated that its inputs
  // changed retires the forecast it found shown - but only that one. A newer one published meanwhile by
  // another refresh stays.
  [Fact]
  public async Task AChangedInputRetiresOnlyTheForecastItFound()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var memory = f.Services.EtaMemory;
    var found = memory.Results[f.Current.Id] with { Signature = "earlier" };
    memory.Results[f.Current.Id] = found;
    var newer = found with
    {
      Signature = "newer",
      Value = found.Value with
      {
        CalculatedAt = found.Value.CalculatedAt.AddSeconds(30),
      },
    };
    f.Publication.BeforeBegin = () =>
    {
      memory.Results[f.Current.Id] = newer;
      throw RoutePlanningException.Changed("Saved roads changed.");
    };

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Same(newer, memory.Results[f.Current.Id]);

    // Without the newer one, the one it found is retired.
    memory.Results[f.Current.Id] = found;
    f.Publication.BeforeBegin = () =>
      throw RoutePlanningException.Changed("Saved roads changed.");
    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );
    Assert.False(memory.Results.ContainsKey(f.Current.Id));
  }

  // A failure that proves nothing about the inputs - here the database, as
  // an unexpected exception at the save - leaves the saved forecast shown,
  // with its inputs unchanged. So does a refusal that is not a change.
  [Theory]
  [InlineData("failed")]
  [InlineData("refused")]
  public async Task AFailureThatProvesNoChangeLeavesTheSavedForecastShown(
    string failure
  )
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var memory = f.Services.EtaMemory;
    var shown = memory.Results[f.Current.Id] with { Signature = "earlier" };
    memory.Results[f.Current.Id] = shown;
    var counted = Counted($"unsaved-{failure}");
    f.Publication.BeforeBegin = () =>
      failure == "failed"
        ? throw new InvalidOperationException("The database went away.")
        : throw new RoutePlanningException("Fuel prices are unavailable.");

    await Assert.ThrowsAnyAsync<Exception>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Same(shown, memory.Results[f.Current.Id]);
    Assert.True(Counted($"unsaved-{failure}") > counted);
  }

  // Other tests count too, in parallel: only increases are asserted.
  private static long Counted(string stage) =>
    PerformanceStages.Snapshot().GetValueOrDefault($"eta-memory/{stage}")?.Items
    ?? 0;
}
