using Domain.Models.Execution;
using Domain.Models.Routing;
using Domain.Rules.Routing;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Routes;

internal static class ExecutionRouteSections
{
  public static async Task<Dictionary<Guid, RouteWorkSnapshot[]>> ReadAsync(
    IAppDbContext db,
    IReadOnlyCollection<Load> sources,
    CancellationToken ct
  )
  {
    var loads = sources.ToDictionary(x => x.Id);
    var ids = loads.Keys.ToArray();
    var links = await db
      .LoadExecutionLegs.AsNoTracking()
      .Include(x => x.ExecutionLeg)
      .Where(x => ids.Contains(x.DispatchId))
      .OrderBy(x => x.Sequence)
      .ThenBy(x => x.Id)
      .ToListAsync(ct);
    // A load's road includes completed legs, unlike its live truck itinerary.
    return links
      .GroupBy(x => x.DispatchId)
      .ToDictionary(
        group => group.Key,
        group =>
          group
            .Where(x => x.ExecutionLeg.Status != "cancelled")
            .Select(x =>
              RouteWorkProjection.Capture(
                loads[group.Key],
                x.ExecutionLeg,
                ExecutionStopRows.Read(x.ExecutionLeg)
              )
            )
            .ToArray()
      );
  }
}
