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
  private readonly object gate = new();
  private readonly Dictionary<Key, Entry> entries = [];
  private int bytes;

  public sealed record Key(Guid Company, Guid Truck, Guid? Dispatch = null);

  public sealed record Work(Key Key, Guid Ticket, string Signature);

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
      if (!entries.TryGetValue(key, out var entry))
      {
        if (entries.Count >= MaximumEntries)
          Remove(entries.MinBy(x => x.Value.RequestedAt).Key);
        entries[key] = entry = new();
      }
      entry.RequestedAt = now;
      if (entry.Signature != signature)
      {
        bytes -= entry.Size;
        entry.Json = null;
        entry.Map = null;
        entry.Signature = signature;
        entry.Ticket = Guid.NewGuid();
        entry.Busy = false;
        entry.RefreshAt = DateTimeOffset.MinValue;
      }
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

  public IReadOnlyList<Work> Committed(Guid company, Guid truck)
  {
    lock (gate)
    {
      var committed = new List<Work>();
      foreach (var pair in entries)
        if (pair.Key.Company == company && pair.Key.Truck == truck)
        {
          pair.Value.Ticket = Guid.NewGuid();
          pair.Value.Busy = false;
          pair.Value.RefreshAt = DateTimeOffset.MinValue;
          committed.Add(new(pair.Key, pair.Value.Ticket, pair.Value.Signature));
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
      entry.Ticket = Guid.NewGuid();
      entry.Busy = true;
      return new(key, entry.Ticket, entry.Signature);
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
      return entries.TryGetValue(work.Key, out var entry)
        && entry.Ticket == work.Ticket;
  }

  public Work? Take()
  {
    lock (gate)
    {
      var now = time.GetUtcNow();
      var pair = entries
        .Where(x =>
          !x.Value.Busy
          && x.Value.RefreshAt <= now
          && x.Value.RequestedAt > now.AddMinutes(-2)
        )
        .OrderBy(x => x.Value.RefreshAt)
        .FirstOrDefault();
      if (pair.Value is null)
        return null;
      pair.Value.Busy = true;
      return new(pair.Key, pair.Value.Ticket, pair.Value.Signature);
    }
  }

  public void Complete(
    Work work,
    string? signature,
    AutomaticPlanningResult? result
  )
  {
    if (!IsCurrent(work))
      return;
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
      if (
        !entries.TryGetValue(work.Key, out var entry)
        || entry.Ticket != work.Ticket
      )
        return;
      entry.Ticket = Guid.NewGuid();
      entry.Busy = false;
      entry.RefreshAt = time.GetUtcNow().AddSeconds(30);
      if (signature is not null && signature != entry.Signature)
      {
        entry.RefreshAt = DateTimeOffset.MinValue;
        return;
      }
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
