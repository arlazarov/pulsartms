using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Diagnostics;
using Domain.Models.Eta;
using Domain.Models.Routing;

namespace Application.Features.Eta.Services;

// What the service remembers of a forecast and hands back on a read: the
// road it was calculated on, the work it belongs to, and whether a reader
// may still see it.
public sealed partial class EtaService
{
  // Keys serialized by this service's reads, for tests that count the work
  // a read does per row, not only its database calls.
  private long keysBuilt;
  internal long KeysBuilt => Interlocked.Read(ref keysBuilt);

  private string RouteKey(RoutePlanningState state)
  {
    Interlocked.Increment(ref keysBuilt);
    return RouteKeyOf(state);
  }

  private string WorkKey(RoutePlanningState state)
  {
    Interlocked.Increment(ref keysBuilt);
    return WorkKeyOf(state);
  }

  private static string RouteKeyOf(RoutePlanningState state) =>
    JsonSerializer.Serialize(
      new
      {
        state.Plan?.Id,
        state.Plan?.ExecutionLegId,
        state.Plan?.AssignmentRevision,
        state.Plan?.Version,
        state.Plan?.Tracking.NextStopId,
        state.Plan?.Tracking.PassedStopIds,
        state.Plan?.InputsChanged,
        state.Plan?.Stops,
        state.Progress?.OffRoute,
        state.Progress?.LocationStale,
      }
    );

  // The load, its leg, the assignment and its stops: what a forecast is a
  // forecast of. The road, its version and the truck's place on it are not
  // part of this - they change while the work stays the same.
  private static string WorkKeyOf(RoutePlanningState state) =>
    JsonSerializer.Serialize(
      new
      {
        state.Plan?.TruckId,
        state.Plan?.DispatchId,
        state.Plan?.ExecutionLegId,
        state.Plan?.AssignmentRevision,
        state.Plan?.Stops,
      }
    );

  // A calculation made unpublished, published once its save committed.
  public void Publish(
    RoutePlanningState state,
    Calculation calculation,
    string? chainInputHash
  ) =>
    Record(
      state,
      calculation.Signature,
      calculation.Value,
      chainInputHash,
      calculation.Driver
    );

  // A forecast is kept against the road it was calculated on and the work
  // it belongs to, so a later read can tell a newer road for the same work
  // from different work.
  public void Record(
    RoutePlanningState state,
    string signature,
    DispatchEta value,
    string? chainInputHash = null,
    string? driver = null
  )
  {
    if (state.Plan is not { } plan)
      return;
    memory.Publish(
      memory.Scope(plan.DispatchId, plan.ExecutionLegId),
      new(signature, value, RouteKey(state))
      {
        ChainInputHash = chainInputHash,
        WorkKey = WorkKey(state),
        Driver = string.IsNullOrEmpty(driver) ? null : driver,
        PlanId = plan.Id,
        PlanVersion = plan.Version,
      }
    );
  }

  // A forecast that is out of date for the same work is not thrown away:
  // it is returned marked as updating, and the worker is woken to replace
  // it. The Dispatch board already reads saved forecasts this way; the map
  // read the same work as having no forecast at all until a new one landed.
  // One for other work - another load, leg, assignment or set of stops - is
  // never shown in its place.
  public DispatchEta? GetCached(RoutePlanningState state)
  {
    if (state.Plan is not { } plan)
      return null;
    var key = memory.Scope(plan.DispatchId, plan.ExecutionLegId);
    var now = DateTime.UtcNow;
    memory.View(key, now);
    memory.Results.TryGetValue(key, out var entry);
    var read = Decide(entry, state, now);
    switch (read.Answer)
    {
      case EtaAnswer.Current:
        MapRead("current");
        memory.NoteMapAnswer(key, "current");
        return entry!.Value;
      case EtaAnswer.Updating:
        MapRead("updating");
        memory.NoteMapAnswer(key, "updating");
        if (read.RouteMatches || memory.SupersedeIfCurrent(key, entry!))
          memory.RequestRefresh();
        return entry!.Value with { RouteUpdatePending = true };
      case EtaAnswer.OtherWork:
        MapRead("other-work");
        var parts = Differing(entry!.WorkKey, read.WorkKey!).ToList();
        foreach (var part in parts)
          MapRead($"other-work-{part}");
        memory.NoteMapAnswer(key, $"other-work:{string.Join(',', parts)}");
        if (memory.RemoveIfCurrent(key, entry))
          memory.RequestRefresh();
        return null;
      default:
        MapRead("no-entry");
        memory.NoteMapAnswer(key, "no-entry");
        return null;
    }
  }

  internal enum EtaAnswer
  {
    None,
    Current,
    Updating,
    OtherWork,
  }

  internal readonly record struct EtaRead(
    EtaAnswer Answer,
    bool RouteMatches,
    string? WorkKey
  );

  // A plan's keys, each serialized at most once however many forecasts
  // are decided against it, and hashed at most once for saved ones.
  internal sealed class StateKeys(EtaService owner, RoutePlanningState state)
  {
    private string? work;
    private string? route;
    private string? workHash;
    private string? routeHash;

    public string Work(bool hashed) =>
      hashed ? workHash ??= Hash(Work(false)) : work ??= owner.WorkKey(state);

    public string Route(bool hashed) =>
      hashed
        ? routeHash ??= Hash(Route(false))
        : route ??= owner.RouteKey(state);
  }

  // What the forecast store keeps of a key: the keys name the plan's stops
  // and can be long.
  private static string Hash(string key) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

  // The keys a saved forecast is stored with, for the plan it was
  // calculated on.
  internal (string Work, string Route) SavedKeys(RoutePlanningState state)
  {
    var keys = new StateKeys(this, state);
    return (keys.Work(true), keys.Route(true));
  }

  // A saved forecast as an entry the one decision reads; none for a row
  // saved without keys (a follower's, or one saved before stage 4e).
  internal static EtaMemory.Entry? Saved(EtaForecastSnapshot? snapshot) =>
    snapshot is { WorkKey: { } work, RouteKey: { } route }
      ? new("", snapshot.Forecast, route) { WorkKey = work, KeysHashed = true }
      : null;

  // The one decision every read makes, with no effects: whether a forecast
  // kept for a plan's scope - in this process' memory or in the store - is
  // a forecast of the same work (truck, load, leg, assignment, stops) and,
  // if so, whether it was made on the same road (saved plan identity and
  // version, tracking, stops, progress) and is still valid. Each key is
  // serialized at most once, the road's only for the same work.
  internal EtaRead Decide(
    EtaMemory.Entry? entry,
    RoutePlanningState state,
    DateTime now
  ) => Decide(entry, new StateKeys(this, state), now);

  private static EtaRead Decide(
    EtaMemory.Entry? entry,
    StateKeys keys,
    DateTime now
  )
  {
    if (entry is null)
      return new(EtaAnswer.None, false, null);
    var work = keys.Work(entry.KeysHashed);
    // The road key does not name the truck, so the work has to match
    // before a forecast counts as current, not only before it is kept.
    if (entry.WorkKey is not { } kept || kept != work)
      return new(EtaAnswer.OtherWork, false, work);
    var route = entry.RouteKey == keys.Route(entry.KeysHashed);
    return new(
      route && !entry.Superseded && entry.Value.ValidUntil > now
        ? EtaAnswer.Current
        : EtaAnswer.Updating,
      route,
      work
    );
  }

  // The forecast a display shows for a plan it already holds - a prepared
  // planning summary - read with no side effects: no demand is marked, no
  // worker woken, nothing dropped or counted, no scope registered. It
  // answers as GetCached does from the same keys, which name the saved
  // plan's identity and version, its tracking, stops and progress - never
  // its display geometry - so a display-trimmed plan reads the same: the
  // current forecast, an older one for the same work marked updating, or
  // none for other work. The worker and the map's own reads keep deciding
  // refreshes (stage 4c).
  public DispatchEta? PeekForDisplay(RoutePlanningState state) =>
    ForDisplay(state, Remembered(state));

  internal EtaMemory.Entry? Remembered(RoutePlanningState state) =>
    state.Plan is { } plan
      ? memory.Results.GetValueOrDefault(plan.ExecutionLegId ?? plan.DispatchId)
      : null;

  // Of the forecasts offered for a plan, the latest calculation of the
  // same work, as the one decision reads it; of two calculated at the same
  // instant, the first offered. One for other work is never chosen, and an
  // older one never over a newer one (stage 4e).
  internal DispatchEta? ForDisplay(
    RoutePlanningState state,
    params EtaMemory.Entry?[] offered
  )
  {
    if (state.Plan is null)
      return null;
    var keys = new StateKeys(this, state);
    var now = DateTime.UtcNow;
    EtaMemory.Entry? chosen = null;
    var answer = EtaAnswer.None;
    foreach (var entry in offered)
    {
      var read = Decide(entry, keys, now);
      if (
        read.Answer is EtaAnswer.Current or EtaAnswer.Updating
        && (
          chosen is null
          || entry!.Value.CalculatedAt > chosen.Value.CalculatedAt
        )
      )
      {
        chosen = entry;
        answer = read.Answer;
      }
    }
    return answer switch
    {
      EtaAnswer.Current => chosen!.Value,
      EtaAnswer.Updating => chosen!.Value with { RouteUpdatePending = true },
      _ => null,
    };
  }

  // Why a map read found a forecast or not, counted for
  // `GET /api/diagnostics/stages` (the open truck 11007 ETA incident): a
  // count per answer, no log line per poll and no change to what is shown.
  private static void MapRead(string answer) =>
    PerformanceStages.Count("eta-map-read", answer, 1);

  private static IEnumerable<string> Differing(string? kept, string read)
  {
    if (kept is null)
      return ["unknown"];
    using var before = JsonDocument.Parse(kept);
    using var now = JsonDocument.Parse(read);
    return
    [
      .. now
        .RootElement.EnumerateObject()
        .Where(x =>
          !before.RootElement.TryGetProperty(x.Name, out var old)
          || old.GetRawText() != x.Value.GetRawText()
        )
        .Select(x => x.Name),
    ];
  }
}
