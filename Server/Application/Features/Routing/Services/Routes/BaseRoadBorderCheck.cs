using Domain.Rules.Ports;
using Domain.Rules.Routing;

namespace Application.Features.Routing.Services.Routes;

// Roads saved before the border check carry no verdict. This gives each one
// its verdict from its own geometry, a few roads per pass, with the local
// region lookup only: no provider call and no replan. A road that leaves
// its country is then reported by BaseRoadBorderRule and bought again, for
// that load only, when the load is next built or tracked.
public sealed class BaseRoadBorderCheck(
  IAppDbContext db,
  IRouteRegionLookup regions
)
{
  // A saved road can be a megabyte of points; a few at a time keeps a pass
  // small.
  public const int PageSize = 5;

  // Returns how many roads were read; none means none is left unchecked.
  public async Task<int> CheckAsync(CancellationToken ct)
  {
    var rows = await db
      .DispatchBaseRoutes.AsNoTracking()
      .Where(x => x.BorderCheck == null)
      .OrderBy(x => x.Id)
      .Select(x => new
      {
        x.Id,
        x.CalculatedAt,
        x.RouteJson,
      })
      .Take(PageSize)
      .ToListAsync(ct);
    foreach (var row in rows)
    {
      var road = SavedRouteReader.Stored(row.RouteJson);
      // Unreadable geometry is recorded as unknown, never as clean.
      var verdict = road is null
        ? BorderVerdict.Unverified
        : RouteBorderPolicy.Check(road, regions, ct);
      // A road saved again meanwhile has its own verdict and is left alone.
      await db
        .DispatchBaseRoutes.Where(x =>
          x.Id == row.Id
          && x.BorderCheck == null
          && x.CalculatedAt == row.CalculatedAt
        )
        .ExecuteUpdateAsync(
          s => s.SetProperty(x => x.BorderCheck, verdict.Stored),
          ct
        );
    }
    return rows.Count;
  }
}
