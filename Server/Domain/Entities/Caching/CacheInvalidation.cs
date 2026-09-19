namespace Domain.Entities.Caching;

// One instance telling the others that a group of cached reads is stale.
// Cache versions live in process memory, so without this record a second
// instance keeps serving reads the first one already dropped.
//
// The log is append-only and read by time window rather than by a cursor.
// A row inserted before another can still become visible after it, and a
// cursor that had already moved past would never see the late one; a window
// wide enough to cover any commit delay sees both. Rows are pruned once they
// are older than any cache entry can still be.
public sealed class CacheInvalidation
{
  public Guid Id { get; set; }

  // The cache group, as the command that invalidated it named it: "board",
  // "dispatch", "route:{load}" and so on.
  public string GroupKey { get; set; } = "";
  public DateTime RecordedAt { get; set; }

  // Which process wrote the row, so an instance skips what it published
  // itself instead of invalidating its own caches a second time.
  public string RecordedBy { get; set; } = "";
}
