using System.Text.Json;
using Application.Features.Execution.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Eta;

public sealed partial class EtaChainInputTests
{
  [Theory]
  [InlineData("base")]
  [InlineData("base-delete")]
  [InlineData("base-clear")]
  [InlineData("connection")]
  [InlineData("connection-delete")]
  [InlineData("connection-clear")]
  [InlineData("root-version")]
  [InlineData("root-tracking")]
  [InlineData("root-owner")]
  [InlineData("root-leg")]
  [InlineData("root-delete")]
  public async Task SavedRoadChangeAtPublicationPreservesPriorEta(string change)
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var before = await SavedForecastsAsync(f);
    Assert.NotEmpty(before);
    var geometryReads = 0;
    f.Publication.BeforeBegin = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      if (change == "base")
        await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.CalculatedAt, DateTime.UtcNow.AddMinutes(1))
        );
      else if (change == "base-clear")
        await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.RouteJson, "")
        );
      else if (change == "base-delete")
        await f.Db.DispatchBaseRoutes.ExecuteDeleteAsync();
      else if (change == "connection")
        await f.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.CalculatedAt, DateTime.UtcNow.AddMinutes(1))
        );
      else if (change == "connection-clear")
        await f.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.RouteJson, (string?)null)
        );
      else if (change == "connection-delete")
        await f.Db.DispatchDeadheads.ExecuteDeleteAsync();
      else if (change == "root-delete")
        await f.Db.DispatchRoutePlans.ExecuteDeleteAsync();
      else
        await ChangeRootAsync(
          f,
          plan =>
          {
            if (change == "root-version")
              plan.Version++;
            else if (change == "root-tracking")
              plan.Tracking.PassedStopIds.Add(f.Current.Stops[^1].Id);
            else if (change == "root-owner")
              plan.TruckId = Guid.NewGuid();
            else
              plan.ExecutionLegId = Guid.NewGuid();
          }
        );
      geometryReads = f.Probe.GeometryReads;
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Contains("Saved roads changed", error.Message);
    Assert.Equal(before, await SavedForecastsAsync(f));
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(geometryReads, f.Probe.GeometryReads);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ColdGeometryMustMatchItsCapturedVersion(bool connection)
  {
    await using var f = await Fixture.CreateAsync();
    var description = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    var road = await f.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync();
    var deadhead = await f.Db.DispatchDeadheads.AsNoTracking().SingleAsync();
    if (connection)
      await f.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
        s.SetProperty(
          x => x.CalculatedAt,
          deadhead.CalculatedAt!.Value.AddMinutes(1)
        )
      );
    else
      await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
        s.SetProperty(x => x.CalculatedAt, road.CalculatedAt.AddMinutes(1))
      );

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.EtaInputs.PrepareAsync(description, default)
    );

    await f.Db.DispatchBaseRoutes.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.CalculatedAt, road.CalculatedAt)
    );
    await f.Db.DispatchDeadheads.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.CalculatedAt, deadhead.CalculatedAt)
    );
    var reads = f.Probe.GeometryReads;
    var retry = await f.Services.EtaInputs.PrepareAsync(description, default);
    Assert.True(f.Probe.GeometryReads > reads);
    var future = Assert.Single(retry.Future);
    Assert.Null(future.UnavailableReason);
    Assert.Equal(3600, Assert.Single(future.Route!.Legs).Seconds);
    Assert.Equal(3600, Assert.Single(future.Connection!.Legs).Seconds);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task MissingRoadArrivalAtPublicationRejectsUnavailableEta(
    bool root
  )
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    var plan = await f.Db.DispatchRoutePlans.AsNoTracking().SingleAsync();
    var road = await f.Db.DispatchBaseRoutes.AsNoTracking().SingleAsync();
    if (root)
      await f.Db.DispatchRoutePlans.ExecuteDeleteAsync();
    else
      await f.Db.DispatchBaseRoutes.ExecuteDeleteAsync();
    f.Db.ChangeTracker.Clear();
    f.Publication.BeforeBegin = async () =>
    {
      if (root)
        f.Db.DispatchRoutePlans.Add(plan);
      else
        f.Db.DispatchBaseRoutes.Add(road);
      await f.Db.SaveChangesAsync();
    };

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Empty(await SavedForecastsAsync(f));
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task StaleCachedRootCannotPublishAgainstNewMetadata()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var before = await SavedForecastsAsync(f);
    await ChangeRootAsync(f, plan => plan.Version++);

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Equal(before, await SavedForecastsAsync(f));
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    f.Services.Reads.Invalidate(RoutePlanStore.CacheKey(f.Current.Id, null));
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.NotEqual(before, await SavedForecastsAsync(f));
  }

  [Fact]
  public async Task PreviouslyCompletedRootStillParticipatesInValidation()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await ChangeRootAsync(f, plan => plan.Tracking.AllStopsPassed = true);
    await f.Services.Forecasts.RefreshAsync(f.Next.Id, default);
    var before = await SavedForecastsAsync(f);
    Assert.Single(before);
    f.Publication.BeforeBegin = () =>
      ChangeRootAsync(f, plan => plan.Tracking.AllStopsPassed = false);

    await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Next.Id, default)
    );

    Assert.Equal(before, await SavedForecastsAsync(f));
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Next.Id));
  }

  [Fact]
  public async Task FuelOnlyRootWriteKeepsTheSameRoadVersion()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    var description = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    f.Publication.BeforeBegin = () =>
      ChangeRootAsync(
        f,
        plan => plan.FuelPlan = new() { CalculatedAt = DateTime.UtcNow }
      );

    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);

    var after = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    Assert.Equal(description.InputHash, after.InputHash);
    Assert.Equal(2, (await SavedForecastsAsync(f)).Length);
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.Equal(1, f.Publication.Calls);
  }

  [Fact]
  public async Task NativeRootMetadataIsCheckedByExecutionLegAtPublication()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    var leg = new ExecutionLeg
    {
      Id = Guid.NewGuid(),
      Trip = new Trip { Id = Guid.NewGuid() },
      TruckId = f.Truck.Id,
      Status = "active",
      Revision = 4,
      Stops = ExecutionStopRows.Capture(f.Current.Stops),
      Loads =
      [
        new()
        {
          Id = Guid.NewGuid(),
          DispatchId = f.Current.Id,
          StartVisitId = f.Current.Stops[0].Id,
          EndVisitId = f.Current.Stops[^1].Id,
        },
      ],
    };
    f.Db.ExecutionLegs.Add(leg);
    await f.Db.SaveChangesAsync();
    var description = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    Assert.Equal(leg.Id, description.RootExecutionLegId);
    var row = await f.Db.DispatchRoutePlans.SingleAsync();
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      row.PlanJson,
      RoutingJson.Options
    )!;
    plan.ExecutionLegId = leg.Id;
    plan.AssignmentRevision = leg.Revision;
    row.ExecutionLegId = leg.Id;
    row.AssignmentRevision = leg.Revision;
    row.InputHash = RoutePlanInputs.Hash(
      description.Loads[0],
      description.Profile
    );
    row.PlanJson = JsonSerializer.Serialize(plan, RoutingJson.Options);
    await f.Db.SaveChangesAsync();
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var before = await SavedForecastsAsync(f);
    // The accepted load in hand and the one assigned after it: the forecast
    // follows the truck's own work instead of stopping at the first leg.
    Assert.Equal(2, before.Length);
    var key = f.Services.EtaMemory.Scope(f.Current.Id, leg.Id);
    f.Publication.BeforeBegin = () =>
      ChangeRootAsync(f, value => value.AssignmentRevision++);

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(key, default)
    );

    Assert.Contains("Saved roads changed", error.Message);
    Assert.Equal(before, await SavedForecastsAsync(f));
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(key));
    Assert.Null(f.Db.Database.CurrentTransaction);
  }

  [Fact]
  public async Task RootOnlyDescriptionIncludesEffectiveRoutingInputs()
  {
    await using var f = await Fixture.CreateAsync();
    await f
      .Db.Dispatches.Where(x => x.Id == f.Next.Id)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "completed"));
    var before = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    Assert.Single(before.Loads);
    var profile = await f.Services.Routes.ProfileAsync(f.Truck.Id, default);
    profile.HeightFeet++;
    await f.Services.Routes.SaveProfileAsync(f.Current.Id, profile, default);

    var after = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;

    Assert.NotEqual(before.InputHash, after.InputHash);
    Assert.Equal(before.GeometryHash, after.GeometryHash);
  }

  [Fact]
  public async Task TrackingObjectKeyOrderDoesNotInvalidateEta()
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await ChangeRootAsync(
      f,
      plan =>
      {
        plan.Tracking.VisitedStops = f
          .Current.Stops.OrderByDescending(x => x.Id)
          .ToDictionary(x => x.Id, _ => DateTime.UtcNow.AddMinutes(-30));
      }
    );
    var before = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    f.Publication.BeforeBegin = () =>
      ChangeRootAsync(
        f,
        plan =>
        {
          plan.Tracking.VisitedStops = plan
            .Tracking.VisitedStops.OrderBy(x => x.Key)
            .ToDictionary(x => x.Key, x => x.Value);
        }
      );

    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);

    var after = (
      await f.Services.EtaInputs.DescribeAsync(f.Truck.Id, default)
    )!;
    Assert.Equal(before.InputHash, after.InputHash);
    Assert.Equal(2, (await SavedForecastsAsync(f)).Length);
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
  }

  private static async Task ChangeRootAsync(Fixture f, Action<RoutePlan> change)
  {
    var json = await f
      .Db.DispatchRoutePlans.Select(x => x.PlanJson)
      .SingleAsync();
    var plan = JsonSerializer.Deserialize<RoutePlan>(
      json,
      RoutingJson.Options
    )!;
    change(plan);
    var updated = JsonSerializer.Serialize(plan, RoutingJson.Options);
    await f.Db.DispatchRoutePlans.ExecuteUpdateAsync(s =>
      s.SetProperty(x => x.PlanJson, updated)
    );
  }

  private static Task<string[]> SavedForecastsAsync(Fixture f) =>
    f
      .Db.Set<DispatchEtaForecast>()
      .AsNoTracking()
      .OrderBy(x => x.DispatchId)
      .Select(x => x.ForecastJson)
      .ToArrayAsync();
}
