using Application.Features.Routing.Exceptions;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Eta;

public sealed partial class EtaChainInputTests
{
  [Theory]
  [InlineData("endpoint")]
  [InlineData("completion")]
  [InlineData("unknown")]
  [InlineData("insert")]
  [InlineData("cancel")]
  public async Task HistoryChangesAtPublicationKeepPriorForecasts(string change)
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    var history = await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var before = await SavedAsync();
    Assert.NotEmpty(before);
    var captured = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    Assert.Single(captured.History.Snapshots);
    f.Publication.BeforeBegin = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      await HistoricalWorkFixture.ChangeAsync(f.Db, history, change);
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Contains("Historical truck work changed", error.Message);
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.Equal(before, await SavedAsync());
    var current = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    Assert.Equal(
      captured.Itinerary.InputSignature,
      current.Itinerary.InputSignature
    );
    Assert.NotEqual(captured.InputHash, current.InputHash);

    async Task<string[]> SavedAsync() =>
      await f
        .Db.Set<DispatchEtaForecast>()
        .AsNoTracking()
        .OrderBy(x => x.DispatchId)
        .Select(x => x.ForecastJson)
        .ToArrayAsync();
  }

  [Fact]
  public async Task HistoricalFactsInvalidateEtaWithoutRecompilingGeometry()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    var history = await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    var before = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    await f.Services.EtaInputs.PrepareAsync(before, default);
    var geometryReads = f.Probe.GeometryReads;

    await HistoricalWorkFixture.ChangeAsync(f.Db, history, "price");
    var after = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    await f.Services.EtaInputs.PrepareAsync(after, default);

    Assert.Equal(
      before.Itinerary.InputSignature,
      after.Itinerary.InputSignature
    );
    Assert.NotEqual(before.InputHash, after.InputHash);
    Assert.Equal(before.GeometryHash, after.GeometryHash);
    Assert.Equal(geometryReads, f.Probe.GeometryReads);
  }

  [Fact]
  public async Task UnavailableHistoryIsStillValidatedAtPublication()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    var history = await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    await HistoricalWorkFixture.ChangeAsync(f.Db, history, "unknown");
    var captured = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    Assert.True(captured.History.Snapshots.Single().HasUnknownStart);
    Assert.Null(captured.Connections[f.Next.Id]);
    f.Publication.BeforeBegin = () =>
      HistoricalWorkFixture.ChangeAsync(f.Db, history, "cancel");

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Empty(await f.Db.Set<DispatchEtaForecast>().ToListAsync());
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
  }

  [Fact]
  public async Task UnchangedHistoryPublishesForecastsWithoutAnExtraScope()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await HistoricalWorkFixture.AddAsync(f.Db, f.Current);

    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);

    Assert.Equal(1, f.Publication.Calls);
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(2, await f.Db.Set<DispatchEtaForecast>().CountAsync());
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
  }
}
