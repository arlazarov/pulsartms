using System.Text;
using System.Text.Json;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Domain.Entities.Dispatch;

namespace Application.Features.Routing.Services.Routes;

public sealed class RouteChoiceDrafts(IAppDbContext db, TimeProvider clock)
{
  public async Task StoreAsync(RouteChoiceDraft draft, CancellationToken ct)
  {
    var json = RoutePlanStorage.Serialize(draft);
    if (Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024)
      throw new RoutePlanningException(
        "The route preview is too large. Try fewer via points."
      );
    var now = clock.GetUtcNow().UtcDateTime;
    await db
      .DispatchRoutePreviews.Where(x => x.ExpiresAt <= now)
      .ExecuteDeleteAsync(ct);
    var row = await db.DispatchRoutePreviews.SingleOrDefaultAsync(
      x => x.Id == draft.Owner,
      ct
    );
    if (row is null)
    {
      row = new DispatchRoutePreview { Id = draft.Owner };
      db.DispatchRoutePreviews.Add(row);
    }
    row.PreviewId = draft.Preview.Id;
    row.ExecutionLegId = draft.Preview.ExecutionLegId;
    row.ExpiresAt = draft.Preview.ExpiresAt;
    row.DraftJson = json;
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateConcurrencyException)
    {
      throw new RoutePlanningException(
        "Another route preview changed. Calculate it again."
      );
    }
    finally
    {
      db.Entry(row).State = EntityState.Detached;
    }
  }

  public async Task<RouteChoiceDraft> GetAsync(
    Guid id,
    Guid owner,
    Guid dispatch,
    CancellationToken ct,
    Guid? executionLegId = null
  )
  {
    var now = clock.GetUtcNow().UtcDateTime;
    var json = await db
      .DispatchRoutePreviews.AsNoTracking()
      .Where(x =>
        x.Id == owner
        && x.PreviewId == id
        && x.ExpiresAt > now
        && x.ExecutionLegId == executionLegId
      )
      .Select(x => x.DraftJson)
      .SingleOrDefaultAsync(ct);
    if (
      json is null
      || json.Length > 8 * 1024 * 1024
      || Encoding.UTF8.GetByteCount(json) > 8 * 1024 * 1024
    )
      throw new RoutePlanningException(
        "The route preview expired. Calculate it again."
      );
    var value = JsonSerializer.Deserialize<RouteChoiceDraft>(
      json,
      RoutingJson.Options
    )!;
    if (
      value.Owner != owner
      || value.Preview.DispatchId != dispatch
      || value.Preview.ExecutionLegId != executionLegId
      || value.Preview.ExpiresAt <= clock.GetUtcNow()
    )
      throw new RoutePlanningException(
        "The route preview expired. Calculate it again."
      );
    return value;
  }
}
