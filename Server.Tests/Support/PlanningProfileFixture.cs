using System.Text.Json;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Fleet;
using Domain.Models.Routing;
using Domain.Rules;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Server.Tests.Support;

internal static class PlanningProfileFixture
{
  public static async Task<string> SaveAsync(
    AppDbContext db,
    Guid truckId,
    TruckRouteProfile profile
  )
  {
    var json = JsonSerializer.Serialize(profile, RoutingJson.Options);
    var changed = await db
      .TruckPlanningProfiles.Where(x => x.TruckId == truckId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.SettingsJson, json));
    if (changed == 0)
    {
      var entity = new TruckPlanningProfile
      {
        Id = Guid.NewGuid(),
        TruckId = truckId,
        SettingsJson = json,
      };
      db.TruckPlanningProfiles.Add(entity);
      await db.SaveChangesAsync();
      db.Entry(entity).State = EntityState.Detached;
    }
    return json;
  }
}
