using Application.Interfaces;
using Domain.Entities.Execution;
using Microsoft.EntityFrameworkCore;

namespace Application.Reference;

// Live trailer handovers, read once for the request.
//
// ExecutionLoads.ReadAsync asked for these on every call, filtered by the
// legs it had just loaded. On this database that was seventeen round trips
// in one board page load against a table holding no rows at all - close to a
// second spent asking whether anything was being handed over.
//
// A handover that is neither cancelled nor finished is a live operation, so
// the set is small by nature rather than by luck. If that stops being true -
// if cancelled rows are kept and this grows into the thousands - this has to
// go back to filtering in the query, and the count of rows loaded here is
// what would say so.
public sealed class ActiveTransfers(IAppDbContext db)
{
  private IReadOnlyList<SwitchParticipant>? all;

  public async Task<IReadOnlyList<SwitchParticipant>> ForLegsAsync(
    IReadOnlyCollection<Guid> legIds,
    CancellationToken ct
  )
  {
    all ??= await db
      .SwitchParticipants.AsNoTracking()
      .Where(x => !x.IsCancelled)
      .ToListAsync(ct);
    if (all.Count == 0 || legIds.Count == 0)
      return [];
    var wanted = legIds as IReadOnlySet<Guid> ?? legIds.ToHashSet();
    return
    [
      .. all.Where(x =>
        wanted.Contains(x.OutgoingLegId) || wanted.Contains(x.IncomingLegId)
      ),
    ];
  }
}
