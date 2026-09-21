using System.Text.Json.Nodes;
using Application.Features.Fuel.Services;
using Application.Features.Routing.Commands;
using Application.Features.Routing.Services.FuelPlanning;
using Application.Models;
using Domain.Models.Routing;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Tests.Support;
using StoredFuel = Domain.Entities.Fuel.TruckFuelPlan;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelHistoricalValidityTests
{
  [Theory]
  [InlineData("endpoint")]
  [InlineData("completion")]
  [InlineData("unknown")]
  [InlineData("insert")]
  [InlineData("cancel")]
  public async Task HistoricalCorrectionsInvalidateWarmFuelWithUnchangedRoads(
    string change
  )
  {
    var columns = new QueryColumnProbe();
    await using var f = await SavedFuelHorizonFixture.CreateAsync(
      queryColumns: columns
    );
    var historical = await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    var profile = await f.PrepareCalculationAsync();
    await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    var state = await f.Services.Routes.GetAsync(f.Current.Id, default);
    await f.Services.FuelPlans.ApplyAsync(state, default);
    Assert.False(state.Plan!.FuelPlan!.NeedsRefresh);
    var before = (await f.Db.Set<StoredFuel>().SingleAsync()).SummaryJson;
    var saved = await f.Services.FuelPlans.ReadAsync(
      state.Plan.TruckId,
      default
    );
    Assert.NotEmpty(saved!.HistoryDependencies!.Batches);
    var work = await f.Services.FuelInputs.ReadFreshAsync(
      state.Plan.TruckId,
      default
    );

    await HistoricalWorkFixture.ChangeAsync(f.Db, historical, change);
    columns.Columns.Clear();
    await f.Services.FuelPlans.ApplyAsync(state, default);

    Assert.True(state.Plan.FuelPlan!.NeedsRefresh);
    Assert.Null(state.Plan.FuelPlan.ScheduleImpact);
    Assert.Empty(state.Plan.FuelPlan.StopArrivals);
    Assert.Contains(
      state.Plan.FuelPlan.RefreshReasons,
      reason => reason.Contains("history", StringComparison.Ordinal)
    );
    Assert.True(
      await f.Services.Roads.MatchesAsync(
        FuelRoadDependencies.Remaining(saved, state.Plan)!,
        default
      )
    );
    var current = await f.Services.FuelInputs.ReadFreshAsync(
      state.Plan.TruckId,
      default
    );
    Assert.Equal(
      work.Itinerary.InputSignature,
      current.Itinerary.InputSignature
    );
    Assert.DoesNotContain(
      columns.Columns.SelectMany(x => x),
      name => name is "RouteJson" or "PlanJson" or "CheckedRouteJson"
    );
    Assert.Equal(
      before,
      (await f.Db.Set<StoredFuel>().SingleAsync()).SummaryJson
    );
    Assert.False(saved.Plan.NeedsRefresh);
    Assert.Equal(0, f.Router.Calls);
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AutomaticRefreshUsesHistoryAndPreservesManualPlans(
    bool manual
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var historical = await HistoricalWorkFixture.AddAsync(f.Db, f.Current);
    var profile = await f.PrepareCalculationAsync();
    var initial = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );
    if (manual)
    {
      var stored = await f.Db.Set<StoredFuel>().SingleAsync();
      var json = JsonNode.Parse(stored.SummaryJson)!;
      json["plan"]!["manuallyEdited"] = true;
      stored.SummaryJson = json.ToJsonString();
      await f.Db.SaveChangesAsync();
    }
    var state = await f.Services.Routes.GetAsync(f.Current.Id, default);
    await HistoricalWorkFixture.ChangeAsync(f.Db, historical, "endpoint");
    var sender = new RefreshRecorder();
    var service = new FuelPriceRefreshService(
      new TruckFuelPlanStore(f.Db, NullLogger<TruckFuelPlanStore>.Instance),
      f.Services.FuelInputs,
      sender,
      new CarrierFuelPrices(sender),
      TimeProvider.System,
      f.Services.SavedFuelInputs
    );

    await service.RefreshAsync(
      new(state.Plan!.TruckId, f.Current.Id, 1, state, null),
      default
    );

    if (manual)
      Assert.Empty(sender.Commands);
    else
      Assert.Equal(f.Current.Id, Assert.Single(sender.Commands).DispatchId);
    Assert.Equal(
      initial.Plan!.CalculatedAt,
      (
        await f.Services.FuelPlans.ReadUncachedAsync(
          state.Plan.TruckId,
          default
        )
      )!.CalculatedAt
    );
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task MissingHistoryEvidenceCannotCertifyALegacyPlan()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    var stored = await f.Db.Set<StoredFuel>().SingleAsync();
    var json = JsonNode.Parse(stored.SummaryJson)!.AsObject();
    json.Remove("historyDependencies");
    stored.SummaryJson = json.ToJsonString();
    await f.Db.SaveChangesAsync();
    var state = await f.Services.Routes.GetAsync(f.Current.Id, default);

    await f.Services.FuelPlans.ApplyAsync(state, default);

    Assert.True(state.Plan!.FuelPlan!.NeedsRefresh);
    Assert.Null(state.Plan.FuelPlan.ScheduleImpact);
    Assert.Equal(0, f.Router.Calls);
  }

  private sealed class RefreshRecorder : ISender
  {
    public List<RecalculateFuelPlanCommand> Commands { get; } = [];

    public Task<TResponse> Send<TResponse>(
      IRequest<TResponse> request,
      CancellationToken cancellationToken = default
    )
    {
      Commands.Add(Assert.IsType<RecalculateFuelPlanCommand>(request));
      object response = RequestResponse<AutomaticPlanningResult>.Ok(
        new(Guid.NewGuid(), null, 0, null, null)
      );
      return Task.FromResult((TResponse)response);
    }

    public Task Send<TRequest>(
      TRequest request,
      CancellationToken cancellationToken = default
    )
      where TRequest : IRequest => throw new NotSupportedException();

    public Task<object?> Send(
      object request,
      CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
      IStreamRequest<TResponse> request,
      CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();

    public IAsyncEnumerable<object?> CreateStream(
      object request,
      CancellationToken cancellationToken = default
    ) => throw new NotSupportedException();
  }
}
