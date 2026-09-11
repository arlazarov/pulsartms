using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Models;
using Domain.Entities.Dispatch;
using Microsoft.EntityFrameworkCore;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Infrastructure.Persistence;

public sealed class NextLoadRouteReader(AppDbContext db) : INextLoadRouteReader
{
  public async Task<IReadOnlyList<Load>> ReadLoadsAsync(Guid truckId, CancellationToken ct) =>
    await db.Dispatches.AsNoTracking().Include(x => x.Stops)
      .Where(x => (x.TruckId == truckId || x.Stops.Any(s => s.TruckId == truckId))
        && (x.Status == "assigned" || x.Status == "in_transit")).ToListAsync(ct);

  public async Task<IReadOnlyList<NextLoadRouteVersion>> ReadVersionsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
    await Versions(ids).ToListAsync(ct);

  private IQueryable<NextLoadRouteVersion> Versions(IReadOnlyCollection<Guid> ids) =>
    Saved(ids).OrderBy(x => x.DispatchId).Select(x => new NextLoadRouteVersion(x.DispatchId,
      x.BaseRoute == null ? null : x.BaseRoute.InputHash, x.BaseRoute == null ? null : (DateTime?)x.BaseRoute.CalculatedAt,
      x.Deadhead == null ? null : (Guid?)x.Deadhead.PreviousDispatchId, x.Deadhead == null ? null : x.Deadhead.InputHash,
      x.Deadhead == null ? null : x.Deadhead.CalculatedAt, x.Deadhead == null ? null : x.Deadhead.Miles));

  public async Task<IReadOnlyDictionary<Guid, SavedNextLoadRoute>> ReadGeometryAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
    await Saved(ids).Select(x => new SavedNextLoadRoute(x.DispatchId, x.BaseRoute, x.Deadhead)).ToDictionaryAsync(x => x.DispatchId, ct);

  private sealed class SavedRow
  {
    public Guid DispatchId { get; init; }
    public DispatchBaseRoute? BaseRoute { get; init; }
    public DispatchDeadhead? Deadhead { get; init; }
  }

  private IQueryable<SavedRow> Saved(IReadOnlyCollection<Guid> ids) =>
    from load in db.Dispatches.AsNoTracking().Where(x => ids.Contains(x.Id))
    join route in db.DispatchBaseRoutes.AsNoTracking() on load.Id equals route.DispatchId into routes
    from route in routes.DefaultIfEmpty()
    join connection in db.DispatchDeadheads.AsNoTracking() on load.Id equals connection.DispatchId into connections
    from connection in connections.DefaultIfEmpty()
    select new SavedRow { DispatchId = load.Id, BaseRoute = route, Deadhead = connection };
}
