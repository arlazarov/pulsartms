using Application.Caching;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Synchronization.Services;
using Domain.Entities.Fleet;
using System.Text.Json;

namespace Application.Features.Routing.Services.Routes;

public sealed class TruckPlanningProfileService(IAppDbContext db, ReadCache reads, PlanningSettingsService settings)
{
  public async Task<TruckRouteProfile> GetAsync(Guid truckId, CancellationToken ct)
  {
    Task<TruckPlanningProfile?> Load() => db.TruckPlanningProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.TruckId == truckId, ct);
    var entity = await reads.GetAsync($"profile:{truckId}", "value", Load);
    var profile = entity is null ? new() : JsonSerializer.Deserialize<TruckRouteProfile>(entity.SettingsJson, RoutePlanningService.Json) ?? new();
    profile.TrailerLengthFeet = TruckRouteProfile.StandardTrailerFeet;
    profile.LengthFeet = TruckRouteProfile.StandardTrailerFeet + 19;
    profile.UsesFleetDefaults = true;
    profile.Mpg = 235.214583 / 35;
    profile.TankGallons = FleetFuelDefaults.TankGallons;
    (await settings.GetAsync(ct)).Preferences.ApplyTo(profile);
    return profile;
  }

  public async Task<TruckRouteProfile> SaveAsync(Guid truckId, TruckRouteProfile profile, CancellationToken ct)
  {
    if (profile.Validate() is { } error) throw new RoutePlanningException(error);
    var entity = await db.TruckPlanningProfiles.SingleOrDefaultAsync(x => x.TruckId == truckId, ct);
    if (entity is null)
    {
      entity = new() { Id = Guid.NewGuid(), TruckId = truckId };
      db.TruckPlanningProfiles.Add(entity);
    }
    entity.SettingsJson = JsonSerializer.Serialize(profile, RoutePlanningService.Json);
    await db.SaveChangesAsync(ct);
    Invalidate(truckId);
    return profile;
  }

  internal void Invalidate(Guid truckId)
  {
    reads.Invalidate($"profile:{truckId}");
    reads.Invalidate("route-previews");
  }
}
