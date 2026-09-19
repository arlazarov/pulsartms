using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Server.Tests.Support;

namespace Server.Tests.Eta;

public sealed partial class EtaChainInputTests
{
  [Theory]
  [InlineData("height")]
  [InlineData("weight")]
  [InlineData("hazmat")]
  public async Task RoutingProfileChangedAtPublicationKeepsPriorEta(
    string changed
  )
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);
    var before = await SavedAsync();
    var profile = await f.Services.Routes.ProfileAsync(f.Truck.Id, default);
    var generation = f.Services.Reads.Generation($"profile:{f.Truck.Id}");
    f.Publication.BeforeBegin = async () =>
    {
      Assert.Null(f.Db.Database.CurrentTransaction);
      if (changed == "height")
        profile.HeightFeet++;
      else if (changed == "weight")
        profile.WeightPounds += 1000;
      else
        profile.Hazmat = "USHazmatClass1";
      await PlanningProfileFixture.SaveAsync(f.Db, f.Truck.Id, profile);
    };

    var error = await Assert.ThrowsAsync<RoutePlanningException>(
      () => f.Services.Forecasts.RefreshAsync(f.Current.Id, default)
    );

    Assert.Contains("Truck routing settings changed", error.Message);
    Assert.Equal(before, await SavedAsync());
    Assert.False(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.Null(f.Db.Database.CurrentTransaction);
    Assert.Equal(
      generation,
      f.Services.Reads.Generation($"profile:{f.Truck.Id}")
    );

    async Task<string[]> SavedAsync() =>
      await f
        .Db.Set<DispatchEtaForecast>()
        .AsNoTracking()
        .OrderBy(x => x.DispatchId)
        .Select(x => x.ForecastJson)
        .ToArrayAsync();
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FuelOnlySettingsDoNotRejectRoadEta(bool fleet)
  {
    await using var f = await Fixture.CreateAsync(
      sender: new DispatchTelemetrySender(new())
    );
    await f.Services.Settings.SaveAsync(new(new(), 0), default);
    var profile = await f.Services.Routes.ProfileAsync(f.Truck.Id, default);
    f.Publication.BeforeBegin = async () =>
    {
      if (fleet)
      {
        var preferences = new PlanningPreferences
        {
          UseIfta = !profile.UseIfta,
          CadToUsd = .81,
        };
        var json = JsonSerializer.Serialize(
          preferences,
          RoutePlanningService.Json
        );
        await f.Db.FleetPlanningSettings.ExecuteUpdateAsync(s =>
          s.SetProperty(x => x.SettingsJson, json)
        );
      }
      else
      {
        profile.Confirmed = !profile.Confirmed;
        await PlanningProfileFixture.SaveAsync(f.Db, f.Truck.Id, profile);
      }
    };

    await f.Services.Forecasts.RefreshAsync(f.Current.Id, default);

    Assert.Equal(2, await f.Db.Set<DispatchEtaForecast>().CountAsync());
    Assert.True(f.Services.EtaMemory.Results.ContainsKey(f.Current.Id));
    Assert.Null(f.Db.Database.CurrentTransaction);
  }
}
