using Domain.Entities.Dispatch;

namespace Application.Features.Dispatch.Services;

public static class DispatchNumbers
{
  public static async Task<IReadOnlyList<int>> ReserveAsync(
    IAppDbContext db,
    IReadOnlyList<int?> preferred,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "Load numbers require the owning creation transaction."
      );
    if (preferred.Count == 0)
      return [];
    var counter =
      db.DispatchNumberCounters.Local.SingleOrDefault()
      ?? await db.DispatchNumberCounters.SingleOrDefaultAsync(
        x => x.Id == DispatchNumberCounter.LoadNumbers,
        ct
      );
    if (counter is null)
    {
      counter = new();
      db.DispatchNumberCounters.Add(counter);
    }
    var highest =
      await db.Dispatches.MaxAsync(x => (int?)x.LoadNumber, ct) ?? 0;
    var requested = preferred.Where(x => x > 0).Select(x => x!.Value).ToArray();
    var used = (
      await db
        .Dispatches.Where(x => requested.Contains(x.LoadNumber))
        .Select(x => x.LoadNumber)
        .ToListAsync(ct)
    ).ToHashSet();
    var pending = db.Dispatches.Local.Select(x => x.LoadNumber).ToArray();
    used.UnionWith(pending);
    var next = Math.Max(counter.NextNumber, (long)highest + 1);
    if (pending.Length > 0)
      next = Math.Max(next, (long)pending.Max() + 1);
    var reserved = new List<int>();
    foreach (var suggested in preferred)
    {
      long value =
        suggested is > 0 && !used.Contains(suggested.Value)
          ? suggested.Value
          : next;
      if (value > int.MaxValue)
        throw new InvalidOperationException("Load numbering is exhausted.");
      reserved.Add((int)value);
      used.Add((int)value);
      next = Math.Max(next, value + 1);
    }
    counter.NextNumber = next;
    return reserved;
  }
}
