using System.Data;
using Application.Features.Dispatch.Queries;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.FuelPlanning;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Integration")]
public sealed class FuelWorkInputsTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task CapturedSignaturesPreserveSavedLegacyAndNativePlans(
    bool native
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    if (native)
      await f.ReceiveCurrentAsync();
    var plan = f.State.Plan!;
    var board = await f.Services.Board.Handle(
      new GetDispatchBoardQuery(
        TruckId: plan.TruckId,
        IncludeOverdue: true,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    var previous = FuelHorizonLoads.SelectLoads(
      plan,
      Assert.Single(board.Response!.Items).Dispatches
    );
    var captured = await f.Services.FuelInputs.ReadFreshAsync(
      plan.TruckId,
      default
    );
    var selected = captured.Select(plan);
    if (native)
    {
      Assert.Equal("Hook", selected[0].Stops[0].Job);
      Assert.Equal("Loaded", selected[0].Stops[0].StateAfter);
    }

    Assert.Equal(
      previous.SelectMany(x => x.Stops).Select(x => (x.Job, x.StateAfter)),
      selected.SelectMany(x => x.Stops).Select(x => (x.Job, x.StateAfter))
    );
    Assert.Equal(
      FuelWorkSignature.Signature(previous),
      FuelWorkSignature.Signature(selected)
    );
    Assert.Equal(
      previous.Select(FuelWorkSignature.LoadSignature),
      selected.Select(FuelWorkSignature.LoadSignature)
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task FreshCaptureCannotReuseTheWarmDisplaySnapshot()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var truck = f.State.Plan!.TruckId;
    var first = (await f.Services.FuelInputs.ReadDisplayAsync(truck, default))!;
    f.Future.Stops[0].Notes = "Changed without a cache notification.";
    await f.Db.SaveChangesAsync();
    var display = (
      await f.Services.FuelInputs.ReadDisplayAsync(truck, default)
    )!;
    var fresh = await f.Services.FuelInputs.ReadFreshAsync(truck, default);

    Assert.Equal(
      first.Itinerary.InputSignature,
      display.Itinerary.InputSignature
    );
    Assert.NotEqual(
      first.Itinerary.InputSignature,
      fresh.Itinerary.InputSignature
    );
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task HorizonUsesCapturedVisitFactsWithoutReloadingAssignments()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var captured = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan!.TruckId,
      default
    );
    var previousName = f.Future.Stops[^1].Name;
    f.Future.Stops[^1].Name = "Changed after capture";
    await f.Db.SaveChangesAsync();

    var old = await f.Horizon.BuildAsync(
      f.State,
      f.State.Profile,
      default,
      captured
    );
    var fresh = await f.Horizon.BuildAsync(f.State, f.State.Profile, default);

    Assert.Equal(previousName, old.Stops[^1].Name);
    Assert.Equal("Changed after capture", fresh.Stops[^1].Name);
    Assert.Equal(old.Route.Miles, fresh.Route.Miles);
    Assert.Equal(0, f.Router.Calls);
  }

  [Theory]
  [InlineData("assigned")]
  [InlineData("planned")]
  public async Task FuelScopeIsIndependentOfDisplayDateAndPlannedFilters(
    string status
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    f.Future.Status = status;
    f.Future.DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
    await f.Db.SaveChangesAsync();
    var plan = f.State.Plan!;
    var board = await f.Services.Board.Handle(
      new GetDispatchBoardQuery(
        TruckId: plan.TruckId,
        IncludeHos: false,
        IncludeFinancials: false,
        IncludeEta: false
      ),
      default
    );
    Assert.DoesNotContain(
      Assert.Single(board.Response!.Items).Dispatches,
      x => x.Id == f.Future.Id
    );
    var captured = await f.Services.FuelInputs.ReadFreshAsync(
      plan.TruckId,
      default
    );

    Assert.Equal(2, captured.Itinerary.Segments.Length);
    Assert.Equal(status == "assigned" ? 2 : 1, captured.Select(plan).Count);
    Assert.Equal(0, f.Router.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task SourceReviewKeepsAcceptedFuelScopeButMissingVisitsBlock(
    bool malformed
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var leg = await f.ReceiveCurrentAsync();
    if (malformed)
      leg.Stops.Clear();
    else
      leg.SourceReviewReason = "Source stop identity changed.";
    await f.Db.SaveChangesAsync();
    var captured = await f.Services.FuelInputs.ReadFreshAsync(
      f.State.Plan!.TruckId,
      default
    );

    if (malformed)
    {
      await Assert.ThrowsAsync<RoutePlanningException>(
        () => f.Horizon.BuildAsync(f.State, f.State.Profile, default, captured)
      );
      Assert.Empty(captured.SelectForDisplay(f.State.Plan));
    }
    else
    {
      var horizon = await f.Horizon.BuildAsync(
        f.State,
        f.State.Profile,
        default,
        captured
      );
      Assert.NotEmpty(horizon.Stops);
      Assert.NotEmpty(captured.SelectForDisplay(f.State.Plan));
    }
    Assert.Equal(0, f.Router.Calls);
  }

  [Fact]
  public async Task FreshCaptureRejectsAnExistingDatabaseSnapshot()
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    await using var transaction = await f.Db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable
    );

    await Assert.ThrowsAsync<InvalidOperationException>(
      () => f.Services.FuelInputs.ReadFreshAsync(f.State.Plan!.TruckId, default)
    );
    Assert.Same(transaction, f.Db.Database.CurrentTransaction);
    Assert.Equal(0, f.Router.Calls);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ChangedInputsDuringSearchPreserveBothSavedCopies(
    bool manual
  )
  {
    await using var f = await SavedFuelHorizonFixture.CreateAsync();
    var profile = await f.PrepareCalculationAsync();
    var initial = await f.Services.Fuel.BuildAsync(
      f.Current.Id,
      new(profile),
      default
    );
    var before = (
      await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()
    ).PlanJson;
    f.BeforePrices = async () =>
    {
      f.BeforePrices = null;
      Assert.Null(f.Db.Database.CurrentTransaction);
      f.Future.Stops[0].Notes = "Changed during fuel search.";
      await f.Db.SaveChangesAsync();
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(async () =>
    {
      if (manual)
        await f.Services.Fuel.EditAsync(
          f.Current.Id,
          new(initial.Plan!.CalculatedAt, []),
          true,
          default
        );
      else
        await f.Services.Fuel.BuildAsync(f.Current.Id, new(profile), default);
    });

    Assert.Contains(
      "Assignments changed during fuel calculation",
      error.Message
    );
    var saved = await new TruckFuelPlanStore(f.Db).ReadAsync(
      f.State.Plan!.TruckId,
      false,
      default
    );
    Assert.Equal(initial.Plan!.CalculatedAt, saved!.Plan!.CalculatedAt);
    Assert.Equal(
      before,
      (await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync()).PlanJson
    );
    Assert.Equal(0, f.Router.Calls);
  }
}
