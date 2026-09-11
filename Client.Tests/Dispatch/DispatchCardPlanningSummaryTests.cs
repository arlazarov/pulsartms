using Client.Models.DTO.Planning;
using Client.Pages.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class DispatchCardPlanningSummaryTests
{
    private static readonly Guid Truck = Guid.NewGuid(), Current = Guid.NewGuid(), Future = Guid.NewGuid(), Other = Guid.NewGuid();

    [Fact]
    public void RemainingBelongsOnlyToTheCurrentLoadAndFuelCountsExactVisitsForEachLoad()
    {
        var result = Result();
        Assert.Equal(new(302, 1), DispatchCardPlanningSummary.From(result, Truck, Current, Current));
        Assert.Equal(new(null, 2), DispatchCardPlanningSummary.From(result, Truck, Current, Future));
        Assert.Equal(default, DispatchCardPlanningSummary.From(result, Truck, Current, Other));
    }

    [Theory]
    [InlineData("result-truck")]
    [InlineData("result-dispatch")]
    [InlineData("plan-truck")]
    [InlineData("plan-dispatch")]
    [InlineData("changed-inputs")]
    [InlineData("completed")]
    [InlineData("no-plan")]
    public void UnrelatedOrInvalidJourneyNeverPublishesLiveFooterValues(string invalid)
    {
        var result = Result();
        switch (invalid)
        {
            case "result-truck": result = result with { TruckId = Other }; break;
            case "result-dispatch": result = result with { DispatchId = Other }; break;
            case "plan-truck": result.State!.Plan!.TruckId = Other; break;
            case "plan-dispatch": result.State!.Plan!.DispatchId = Other; break;
            case "changed-inputs": result.State!.Plan!.InputsChanged = true; break;
            case "completed": result.State!.Plan!.Tracking.AllStopsPassed = true; break;
            case "no-plan": result = result with { State = result.State! with { Plan = null } }; break;
        }
        Assert.Equal(default, DispatchCardPlanningSummary.From(result, Truck, Current, Current));
        Assert.Equal(default, DispatchCardPlanningSummary.From(result, Truck, Current, Future));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("refresh")]
    [InlineData("wrong-truck")]
    [InlineData("wrong-current")]
    [InlineData("unlisted-load")]
    public void UnknownFuelPlanDoesNotBecomeZeroStops(string invalid)
    {
        var result = Result();
        var fuel = result.State!.Plan!.FuelPlan!;
        switch (invalid)
        {
            case "absent": result.State.Plan.FuelPlan = null; break;
            case "refresh": fuel.NeedsRefresh = true; break;
            case "wrong-truck": fuel.TruckId = Other; break;
            case "wrong-current": fuel.DispatchIds.Remove(Current); break;
            case "unlisted-load": fuel.DispatchIds.Remove(Future); break;
        }
        Assert.Null(DispatchCardPlanningSummary.From(result, Truck, Current, Future).FuelStopCount);
        Assert.Equal(302, DispatchCardPlanningSummary.From(result, Truck, Current, Current).RemainingMiles);
    }

    [Fact]
    public void KnownEmptyPlanIsZeroButMissingOrInvalidProgressIsNotAnInventedRemainingDistance()
    {
        var result = Result();
        result.State!.Plan!.FuelPlan!.Stops.Clear();
        Assert.Equal(0, DispatchCardPlanningSummary.From(result, Truck, Current, Future).FuelStopCount);
        foreach (var remaining in new double?[] { null, -1, double.NaN, double.PositiveInfinity })
        {
            var noProgress = result with { State = result.State with { Progress = result.State.Progress! with { RemainingMiles = remaining } } };
            Assert.Null(DispatchCardPlanningSummary.From(noProgress, Truck, Current, Current).RemainingMiles);
        }
        result = result with { State = result.State with { Progress = null } };
        Assert.Null(DispatchCardPlanningSummary.From(result, Truck, Current, Current).RemainingMiles);
    }

    [Fact]
    public void MissingOwnerIdentityNeverBorrowsAnOtherwiseMatchingPlan()
    {
        var result = Result();
        Assert.Equal(default, DispatchCardPlanningSummary.From(null, Truck, Current, Current));
        Assert.Equal(default, DispatchCardPlanningSummary.From(result, null, Current, Current));
        Assert.Equal(default, DispatchCardPlanningSummary.From(result, Truck, null, Current));
        Assert.Equal(default, DispatchCardPlanningSummary.From(result, Truck, Current, Guid.Empty));
    }

    private static AutomaticPlanningResult Result()
    {
        var plan = new RoutePlan
        {
            TruckId = Truck, DispatchId = Current,
            FuelPlan = new()
            {
                TruckId = Truck, DispatchIds = [Current, Future],
                Stops = [new() { DispatchId = Current }, new() { DispatchId = Future }, new() { DispatchId = Other }, new() { DispatchId = Future }]
            }
        };
        return new(Truck, Current, 1375, new(new(), plan, new(20, 302, null, 0, false, false, null, null), null, null, false), null);
    }
}
