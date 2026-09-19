using Application.Caching;
using Application.Features.Synchronization.Services;
using Application.Features.Routing.Exceptions;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Features.Routing.Models;
using Domain.Entities.Fleet;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningSettingsService(IAppDbContext db, ReadCache reads, ProcessGates gates)
{
  private static readonly Guid SettingsId = new("6f65ae4c-a62e-47cf-b84b-e1d5f89b908f");
  private readonly SemaphoreSlim saveGate = gates.Single<PlanningSettingsService>();

  public async Task<PlanningSettingsState> GetAsync(CancellationToken ct)
  {
    var entity = await reads.GetAsync("settings", "fleet", Load);
    Task<FleetPlanningSettings?> Load() => db.FleetPlanningSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == SettingsId, ct);
    PlanningPreferences stored = entity is null ? new() : JsonSerializer.Deserialize<PlanningPreferences>(entity.SettingsJson, RoutePlanningService.Json)!;
    return new(FleetFuelDefaults.Apply(stored), entity?.Revision ?? 0, entity?.UpdatedAt);
  }

  public async Task<PlanningSettingsState> SaveAsync(PlanningSettingsUpdate request, CancellationToken ct)
  {
    var errors = new List<ValidationResult>();
    if (request.Preferences is null || !Validator.TryValidateObject(request.Preferences, new ValidationContext(request.Preferences), errors, true))
      throw new RoutePlanningException(string.Join(" ", errors.Select(x => x.ErrorMessage).DefaultIfEmpty("Planning preferences are required.")));
    var preferences = FleetFuelDefaults.Apply(request.Preferences);
    await saveGate.WaitAsync(ct);
    try
    {
      var entity = await db.FleetPlanningSettings.SingleOrDefaultAsync(x => x.Id == SettingsId, ct);
      if (request.Revision != (entity?.Revision ?? 0)) throw new PlanningSettingsConflictException();
      var json = JsonSerializer.Serialize(preferences, RoutePlanningService.Json);
      if (entity is not null && entity.SettingsJson == json)
        return new(preferences, entity.Revision, entity.UpdatedAt);
      if (entity is null)
      {
        entity = new() { Id = SettingsId };
        db.FleetPlanningSettings.Add(entity);
      }
      entity.SettingsJson = json;
      entity.Revision++;
      entity.UpdatedAt = DateTime.UtcNow;
      await db.SaveChangesAsync(ct);
      reads.Invalidate("settings");
      return new(preferences, entity.Revision, entity.UpdatedAt);
    }
    finally { saveGate.Release(); }
  }

  public static string Signature(TruckRouteProfile profile) => Convert.ToHexString(SHA256.HashData(
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(PlanningPreferences.From(profile), RoutePlanningService.Json))));
}
