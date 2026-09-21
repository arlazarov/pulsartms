using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Policies;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Options;

namespace Application.Features.Routing.Services.Routes;

public sealed class RouteRecalculationBudget(
  IAppDbContext db,
  IOptions<RouteRecalculationBudgetOptions> options
)
{
  private static readonly SemaphoreSlim Gate = new(1);

  public static DateTime? BlockedUntil(
    IReadOnlyList<RouteRecalculationAttempt> attempts,
    RoutePoint position,
    DateTime now
  )
  {
    var day = attempts
      .Where(x => x.CreatedAt > now.AddDays(-1))
      .OrderBy(x => x.CreatedAt)
      .ToArray();
    if (day.Length >= 12)
      return day[^12].CreatedAt.AddDays(1);
    var hour = day.Where(x => x.CreatedAt > now.AddHours(-1)).ToArray();
    if (hour.Length >= 3)
      return hour[^3].CreatedAt.AddHours(1);
    var last = attempts.MaxBy(x => x.CreatedAt);
    if (last is null)
      return null;
    if (last.CreatedAt.AddMinutes(15) > now)
      return last.CreatedAt.AddMinutes(15);
    return
      RouteGeometry.Distance(new(last.Latitude, last.Longitude), position) < 3
      ? now.AddMinutes(15)
      : null;
  }

  public async Task ReserveAsync(
    Guid truckId,
    RoutePoint position,
    CancellationToken ct
  )
  {
    ct.ThrowIfCancellationRequested();
    if (!options.Value.Enabled)
      return;
    await GateWait.WaitAsync(Gate, "RouteBudget", ct);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(ct);
      await db.LockRouteBudgetAsync(ct);
      var now = DateTime.UtcNow;
      var attempts = await db
        .RouteRecalculationAttempts.AsNoTracking()
        .Where(x => x.TruckId == truckId && x.CreatedAt > now.AddDays(-1))
        .ToListAsync(ct);
      var latest = await db
        .RouteRecalculationAttempts.AsNoTracking()
        .Where(x => x.TruckId == truckId)
        .OrderByDescending(x => x.CreatedAt)
        .FirstOrDefaultAsync(ct);
      if (latest is not null && attempts.All(x => x.Id != latest.Id))
        attempts.Add(latest);
      if (BlockedUntil(attempts, position, now) is { } retry)
        throw new RoutePlanningException(
          "Automatic route update paused by the truck request budget. The saved route is retained.",
          retry
        );
      db.RouteRecalculationAttempts.Add(
        new()
        {
          Id = Guid.NewGuid(),
          TruckId = truckId,
          CreatedAt = now,
          Latitude = position.Latitude,
          Longitude = position.Longitude,
        }
      );
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
    }
    finally
    {
      Gate.Release();
    }
  }
}
