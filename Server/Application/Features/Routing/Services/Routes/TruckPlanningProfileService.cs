using System.Text.Json;
using Application.Caching;
using Application.Features.Fuel.Services;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

public sealed class TruckPlanningProfileService(
  IAppDbContext db,
  ReadCache reads,
  PlanningSettingsService settings,
  FuelExchangeRateService exchangeRates
)
{
  public Task<TruckRouteProfile> GetAsync(Guid truckId, CancellationToken ct) =>
    ReadAsync(truckId, ct, cached: true);

  public async Task<IReadOnlyDictionary<Guid, TruckRouteProfile>> GetManyAsync(
    IReadOnlyCollection<Guid> truckIds,
    CancellationToken ct
  )
  {
    var ids = truckIds.Distinct().ToArray();
    var rows = await reads.GetManyAsync<TruckPlanningProfile>(
      "profile-rows",
      ids,
      "value",
      async missing =>
        await db
          .TruckPlanningProfiles.AsNoTracking()
          .Where(x => missing.Contains(x.TruckId))
          .ToDictionaryAsync(x => x.TruckId, ct),
      ct
    );
    var result = new Dictionary<Guid, TruckRouteProfile>();
    foreach (var id in ids)
      result[id] = await ResolveAsync(rows.GetValueOrDefault(id), ct, true);
    return result;
  }

  public Task<TruckRouteProfile> GetUncachedAsync(
    Guid truckId,
    CancellationToken ct
  ) => ReadAsync(truckId, ct, cached: false);

  private async Task<TruckRouteProfile> ReadAsync(
    Guid truckId,
    CancellationToken ct,
    bool cached
  )
  {
    ct.ThrowIfCancellationRequested();
    if (cached)
      return (await GetManyAsync([truckId], ct))[truckId];
    var entity = await db
      .TruckPlanningProfiles.AsNoTracking()
      .SingleOrDefaultAsync(x => x.TruckId == truckId, ct);
    return await ResolveAsync(entity, ct, cached);
  }

  private async Task<TruckRouteProfile> ResolveAsync(
    TruckPlanningProfile? entity,
    CancellationToken ct,
    bool cached
  )
  {
    var profile = entity is null
      ? new()
      : JsonSerializer.Deserialize<TruckRouteProfile>(
        entity.SettingsJson,
        RoutingJson.Options
      ) ?? new();
    profile.TrailerLengthFeet = TruckRouteProfile.StandardTrailerFeet;
    profile.LengthFeet = TruckRouteProfile.StandardTrailerFeet + 19;
    profile.UsesFleetDefaults = true;
    profile.Mpg = 235.214583 / 35;
    profile.TankGallons = FleetFuelDefaults.TankGallons;
    var preferences = cached
      ? await settings.GetAsync(ct)
      : await settings.GetUncachedAsync(ct);
    preferences.Preferences.ApplyTo(profile);
    var rate =
      profile.CadToUsd.HasValue ? null
      : cached ? await exchangeRates.ReadAsync(ct)
      : await exchangeRates.ReadUncachedAsync(ct);
    if (profile.CadToUsd is null && rate is not null)
      profile.CadToUsd = (double)rate.UsdPerCad;
    return profile;
  }

  internal async Task RequireCurrentAsync(
    Guid truckId,
    TruckRouteProfile expected,
    CancellationToken ct
  )
  {
    var current = await GetUncachedAsync(truckId, ct);
    if (
      JsonSerializer.Serialize(current, RoutingJson.Options)
      != JsonSerializer.Serialize(expected, RoutingJson.Options)
    )
      throw new RoutePlanningException(
        "Truck planning settings changed. Recalculate the plan."
      );
  }

  internal Task RequireRoutingCurrentAsync(
    Load load,
    TruckRouteProfile expected,
    CancellationToken ct
  ) =>
    RequireRoutingCurrentAsync(
      RouteWorkProjection.Capture(load.TruckItinerary()),
      expected,
      ct
    );

  internal async Task RequireRoutingCurrentAsync(
    RouteWorkSnapshot load,
    TruckRouteProfile expected,
    CancellationToken ct
  )
  {
    var current = await GetUncachedAsync(load.TruckId ?? Guid.Empty, ct);
    if (
      RoutePlanInputs.Hash(load, current)
      != RoutePlanInputs.Hash(load, expected)
    )
      throw new RoutePlanningException(
        "Truck routing settings changed. Recalculate the plan."
      );
  }

  internal async Task<TruckRouteProfile> SaveAsync(
    Guid truckId,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Profile writes require a planning publication transaction."
      );
    if (profile.Validate() is { } error)
      throw new RoutePlanningException(error);
    var entity = await db.TruckPlanningProfiles.SingleOrDefaultAsync(
      x => x.TruckId == truckId,
      ct
    );
    if (entity is null)
    {
      entity = new() { Id = Guid.NewGuid(), TruckId = truckId };
      db.TruckPlanningProfiles.Add(entity);
    }
    entity.SettingsJson = JsonSerializer.Serialize(
      profile,
      RoutingJson.Options
    );
    await db.SaveChangesAsync(ct);
    return profile;
  }

  internal void Invalidate(Guid truckId)
  {
    reads.Invalidate($"profile:{truckId}");
    reads.InvalidateItem("profile-rows", truckId);
    reads.InvalidateItem("planning-inputs", truckId);
    reads.Invalidate("route-previews");
  }
}
