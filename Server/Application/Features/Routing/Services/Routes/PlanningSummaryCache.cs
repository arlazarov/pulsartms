using System.IO.Compression;
using System.Text.Json;
using Application.Models;
using Domain.Models.Routing;
using Domain.Rules;

namespace Application.Features.Routing.Services.Routes;

public sealed class PlanningSummaryCache(TimeProvider time) : ICacheMemorySource
{
  private const int MaximumEntries = 256;
  private const int MaximumEntryBytes = 512 * 1024;
  private const int MaximumBytes = 8 * 1024 * 1024;

  // A preparation that never completes - its holder gone without a word -
  // stops holding the entry after this, so the entry is prepared again.
  private static readonly TimeSpan LeaseLimit = TimeSpan.FromMinutes(5);
  private readonly object gate = new();
  private readonly Dictionary<Key, Entry> entries = [];
  private int bytes;

  public sealed record Key(Guid Company, Guid Truck, Guid? Dispatch = null);

  // Ticket: the version of the entry this work may publish to; a commit or
  // other inputs change it. Lease: the one preparation the entry is waiting
  // on; a commit leaves it with that preparation, which releases it when it
  // completes, however late - so changes during a preparation add one more
  // preparation after it, not one alongside it for each change.
  public sealed record Work(
    Key Key,
    Guid Ticket,
    string Signature,
    Guid Lease = default
  );

  private sealed class Entry
  {
    public Guid Ticket { get; set; } = Guid.NewGuid();
    public string Signature { get; set; } = "";
    public byte[]? Json { get; set; }
    public byte[]? Map { get; set; }
    public Guid? DispatchId { get; set; }
    public Guid? PlanId { get; set; }
    public int? PlanVersion { get; set; }
    public int Size => (Json?.Length ?? 0) + (Map?.Length ?? 0);
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset RefreshAt { get; set; }
    public bool Busy { get; set; }
    public Guid Lease { get; set; }
    public DateTimeOffset LeasedAt { get; set; }
  }

  public AutomaticPlanningResult? Read(
    Key key,
    string signature,
    bool geometry = true,
    Guid? knownPlanId = null,
    int? knownVersion = null
  )
  {
    byte[]? json;
    bool refreshing;
    bool compressed;
    lock (gate)
    {
      var now = time.GetUtcNow();
      var entry = Request(key, signature, now);
      compressed =
        geometry
        && entry.Map is not null
        && (knownPlanId != entry.PlanId || knownVersion != entry.PlanVersion);
      json = compressed ? entry.Map : entry.Json;
      refreshing = entry.Busy || entry.RefreshAt <= now;
    }
    AutomaticPlanningResult? result = null;
    if (json is not null)
    {
      if (compressed)
      {
        using var input = new MemoryStream(json, writable: false);
        using var stream = new BrotliStream(input, CompressionMode.Decompress);
        result = JsonSerializer.Deserialize<AutomaticPlanningResult>(
          stream,
          RoutingJson.Options
        );
      }
      else
        result = JsonSerializer.Deserialize<AutomaticPlanningResult>(
          json,
          RoutingJson.Options
        );
    }
    return result is null
      ? null
      : result with
      {
        IsRefreshing =
          refreshing || result.CalculatedAt < time.GetUtcNow().AddSeconds(-30),
      };
  }

  // Work that is running is kept prepared whether or not a page is open:
  // the background asks for it exactly as a reader would, and the same
  // consumers prepare it on the same path. Nothing is decoded, because
  // nobody is reading it yet.
  public void Keep(Key key, string signature)
  {
    lock (gate)
      Request(key, signature, time.GetUtcNow());
  }

  private Entry Request(Key key, string signature, DateTimeOffset now)
  {
    if (!entries.TryGetValue(key, out var entry))
    {
      if (entries.Count >= MaximumEntries)
        Remove(entries.MinBy(x => x.Value.RequestedAt).Key);
      entries[key] = entry = new();
    }
    entry.RequestedAt = now;
    // Other work - another assignment, other settings - never inherits this
    // snapshot, not even while its own is being prepared.
    if (entry.Signature != signature)
    {
      bytes -= entry.Size;
      entry.Json = null;
      entry.Map = null;
      entry.Signature = signature;
      entry.Ticket = Guid.NewGuid();
      entry.RefreshAt = DateTimeOffset.MinValue;
    }
    return entry;
  }

  // A reader found the prepared summary behind a dependency its signature
  // does not name (the driver's duty): prepare it again soon. A preparation
  // under way keeps its ticket - its result is still the latest prepared,
  // and the next reader asks again if it is behind too.
  public void Due(Key key, string signature)
  {
    lock (gate)
      if (
        entries.TryGetValue(key, out var entry)
        && entry.Signature == signature
        && !entry.Busy
      )
        entry.RefreshAt = DateTimeOffset.MinValue;
  }

  // A stored truck summary as the consistency audit compares it with the
  // owner's current work: the signature it was stored under and the
  // dispatch it speaks for. Process-local, like the cache.
  public sealed record Stored(Guid Truck, string Signature, Guid? DispatchId);

  public IReadOnlyList<Stored> StoredFor(Guid company)
  {
    lock (gate)
      return
      [
        .. entries
          .Where(x =>
            x.Key.Company == company
            && x.Key.Dispatch is null
            && x.Value.Json is not null
          )
          .Select(x => new Stored(
            x.Key.Truck,
            x.Value.Signature,
            x.Value.DispatchId
          )),
      ];
  }

  public IReadOnlyList<Work> Committed(Guid company, Guid truck)
  {
    lock (gate)
    {
      var committed = new List<Work>();
      foreach (var pair in entries)
        if (pair.Key.Company == company && pair.Key.Truck == truck)
        {
          pair.Value.Ticket = Guid.NewGuid();
          pair.Value.RefreshAt = DateTimeOffset.MinValue;
          committed.Add(
            new(
              pair.Key,
              pair.Value.Ticket,
              pair.Value.Signature,
              pair.Value.Busy ? pair.Value.Lease : default
            )
          );
        }
      return committed;
    }
  }

  public Work? Capture(Key key, string signature)
  {
    lock (gate)
    {
      if (
        !entries.TryGetValue(key, out var entry)
        || entry.Signature != signature
        || entry.RequestedAt <= time.GetUtcNow().AddMinutes(-2)
      )
        return null;
      var lease = Lease(entry);
      entry.Ticket = Guid.NewGuid();
      return new(key, entry.Ticket, entry.Signature, lease);
    }
  }

  public IReadOnlyList<Work> CaptureDispatch(Guid company, Guid dispatch)
  {
    lock (gate)
      return entries
        .Where(x =>
          x.Key.Company == company
          && (x.Key.Dispatch ?? x.Value.DispatchId) == dispatch
          && x.Value.RequestedAt > time.GetUtcNow().AddMinutes(-2)
        )
        .Select(x => Capture(x.Key, x.Value.Signature)!)
        .ToArray();
  }

  public bool IsCurrent(Work work)
  {
    lock (gate)
      return entries.TryGetValue(work.Key, out var entry) && Holds(entry, work);
  }

  // The work's ticket is the entry's, and so is its lease if it has one.
  private static bool Holds(Entry entry, Work work) =>
    entry.Ticket == work.Ticket
    && (work.Lease == default || entry.Lease == work.Lease);

  public Work? Take()
  {
    lock (gate)
    {
      var now = time.GetUtcNow();
      var pair = entries
        .Where(x =>
          (!x.Value.Busy || x.Value.LeasedAt <= now - LeaseLimit)
          && x.Value.RefreshAt <= now
          && x.Value.RequestedAt > now.AddMinutes(-2)
        )
        .OrderBy(x => x.Value.RefreshAt)
        .FirstOrDefault();
      if (pair.Value is null)
        return null;
      var lease = Lease(pair.Value);
      return new(pair.Key, pair.Value.Ticket, pair.Value.Signature, lease);
    }
  }

  private Guid Lease(Entry entry)
  {
    // Taking over a lease that ran out: the earlier holder's ticket goes
    // with it, so its late result cannot be published.
    if (entry.Busy)
      entry.Ticket = Guid.NewGuid();
    entry.Busy = true;
    entry.Lease = Guid.NewGuid();
    entry.LeasedAt = time.GetUtcNow();
    return entry.Lease;
  }

  // Work that may no longer publish still ends its lease, so the entry is
  // prepared again once, after it.
  private void Release(Work work)
  {
    lock (gate)
      if (
        work.Lease != default
        && entries.TryGetValue(work.Key, out var entry)
        && entry.Busy
        && entry.Lease == work.Lease
      )
        entry.Busy = false;
  }

  public void Complete(
    Work work,
    string? signature,
    AutomaticPlanningResult? result
  )
  {
    if (!IsCurrent(work))
    {
      Release(work);
      return;
    }
    byte[]? json = null;
    byte[]? map = null;
    var plan = result?.State?.Plan;
    if (result is not null)
    {
      if (plan is not null)
      {
        using var output = new MemoryStream();
        using (
          var stream = new BrotliStream(
            output,
            CompressionLevel.Fastest,
            leaveOpen: true
          )
        )
          JsonSerializer.Serialize(stream, result, RoutingJson.Options);
        map = output.ToArray();
        using var input = new MemoryStream(map, writable: false);
        using var decoded = new BrotliStream(input, CompressionMode.Decompress);
        result = JsonSerializer.Deserialize<AutomaticPlanningResult>(
          decoded,
          RoutingJson.Options
        )!;
        PlanningReadService.TrimForDisplay(
          result.State!.Plan!,
          plan.Id,
          plan.Version
        );
      }
      json = JsonSerializer.SerializeToUtf8Bytes(result, RoutingJson.Options);
    }
    lock (gate)
    {
      if (!entries.TryGetValue(work.Key, out var entry))
        return;
      if (!Holds(entry, work))
      {
        if (work.Lease != default && entry.Lease == work.Lease)
          entry.Busy = false;
        return;
      }
      entry.Ticket = Guid.NewGuid();
      entry.Busy = false;
      entry.RefreshAt = time.GetUtcNow().AddSeconds(30);
      // Prepared for other inputs than the entry's: not stored, and not
      // prepared again at once either - that would only repeat the same
      // mismatch until a reader asks with the new inputs, and a reader that
      // does makes the entry due immediately.
      if (signature is not null && signature != entry.Signature)
        return;
      if (json is null || json.Length + (map?.Length ?? 0) > MaximumEntryBytes)
        return;
      bytes -= entry.Size;
      entry.Json = json;
      entry.Map = map;
      entry.DispatchId = result?.DispatchId;
      entry.PlanId = plan?.Id;
      entry.PlanVersion = plan?.Version;
      entry.Signature = signature!;
      bytes += entry.Size;
      while (bytes > MaximumBytes)
        Remove(entries.MinBy(x => x.Value.RequestedAt).Key);
    }
  }

  public IReadOnlyList<CacheMemorySnapshot> ReadMemory()
  {
    lock (gate)
      return
      [
        new("planning-summaries", entries.Count, bytes, MaximumBytes, "bytes"),
      ];
  }

  private void Remove(Key key)
  {
    bytes -= entries[key].Size;
    entries.Remove(key);
  }
}
