using System.Diagnostics;
using Application.Caching;
using Application.Diagnostics;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Interfaces;
using Application.Features.Routing.Services.Addresses;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Dispatch;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Logging;
using Load = Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Routing.Services.Deadheads;

public sealed partial class DeadheadService(
  IAppDbContext db,
  IRoutingProvider routing,
  RoutePlanningService plans,
  DispatchRates financials,
  DeadheadHistoryService historyReader,
  DeadheadHistoryPublication publication,
  ILogger<DeadheadService> logger
)
{
  private static readonly KeyedGates Gates = new();

  public Task<
    IReadOnlyDictionary<Guid, DeadheadHistorySnapshot>
  > ReadHistoryAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
    historyReader.ReadAsync(ids, ct);

  public async Task<(
    TruckRoute? Route,
    DeadheadHistoryBatch History,
    SavedRoadVersion Road
  )> CaptureRouteAsync(
    Guid previousId,
    RouteWorkSnapshot current,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var history = await historyReader.ReadLoadedAsync([current], ct);
    var (route, road) = await ReadCapturedRouteAsync(
      previousId,
      current,
      profile,
      history,
      ct
    );
    return (route, new([history[current.Id]]), road);
  }

  internal async Task<(
    TruckRoute? Route,
    SavedRoadVersion Road
  )> ReadCapturedRouteAsync(
    Guid previousId,
    RouteWorkSnapshot current,
    TruckRouteProfile profile,
    IReadOnlyDictionary<Guid, DeadheadHistorySnapshot> history,
    CancellationToken ct
  )
  {
    var saved = await db
      .DispatchDeadheads.AsNoTracking()
      .SingleOrDefaultAsync(
        x =>
          x.DispatchId == current.Id
          && x.ExecutionLegId == current.ExecutionLegId,
        ct
      );
    var pair = DeadheadConnection.Find(history.GetValueOrDefault(current.Id));
    var route =
      pair?.Previous.Id == previousId ? pair.ReadRoute(saved, profile) : null;
    var road = SavedRoadVersion.Connection(
      NextLoadRouteVersion.From(
        current.Id,
        current.ExecutionLegId,
        new(current.Id, null, saved)
      )
    );
    return (route, road);
  }

  private Task<Dictionary<Guid, DispatchDeadhead>> ReadSavedAsync(
    IReadOnlyCollection<Guid> ids,
    CancellationToken ct
  ) =>
    db
      .DispatchDeadheads.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId) && x.ExecutionLegId == null)
      .ToDictionaryAsync(x => x.DispatchId, ct);

  public Task<TruckRoute?> ReadRouteAsync(
    Guid previousId,
    Load current,
    TruckRouteProfile profile,
    CancellationToken ct
  ) =>
    ReadRouteAsync(
      previousId,
      RouteWorkProjection.Capture(current),
      profile,
      ct
    );

  public async Task<TruckRoute?> ReadRouteAsync(
    Guid previousId,
    RouteWorkSnapshot current,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var routes = await ReadRoutesAsync(current.TruckId, [current], profile, ct);
    return
      routes.TryGetValue(current.Id, out var route)
      && route.PreviousId == previousId
      ? route.Route
      : null;
  }

  public async Task<
    Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>
  > ReadRoutesAsync(
    Guid? truckId,
    IReadOnlyCollection<RouteWorkSnapshot> loads,
    TruckRouteProfile profile,
    CancellationToken ct
  )
  {
    var result = new Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>();
    if (truckId is null || loads.Count == 0)
      return result;
    var history = await historyReader.ReadLoadedAsync(loads, ct);
    return await ReadRoutesAsync(loads, profile, history, ct);
  }

  public async Task<
    Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>
  > ReadRoutesAsync(
    IReadOnlyCollection<RouteWorkSnapshot> loads,
    TruckRouteProfile profile,
    IReadOnlyDictionary<Guid, DeadheadHistorySnapshot> history,
    CancellationToken ct
  )
  {
    var result = new Dictionary<Guid, (Guid PreviousId, TruckRoute Route)>();
    var ids = loads.Select(x => x.Id).Distinct().ToArray();
    var saved = await db
      .DispatchDeadheads.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId))
      .ToDictionaryAsync(x => (x.DispatchId, x.ExecutionLegId), ct);
    foreach (var load in loads)
    {
      var pair = DeadheadConnection.Find(history.GetValueOrDefault(load.Id));
      var route = pair?.ReadRoute(
        saved.GetValueOrDefault((load.Id, load.ExecutionLegId)),
        profile
      );
      if (route is not null)
        result[load.Id] = (pair!.Previous.Id, route);
    }
    return result;
  }

  public async Task ReadAsync(
    IReadOnlyCollection<DispatchResponse> items,
    CancellationToken ct
  )
  {
    if (items.Count == 0)
      return;
    var ids = items.Select(x => x.Id).ToArray();
    // Totals are cumulative, so this request's share is the difference. A
    // running total printed as if it were one request reads as a number that
    // grows on its own.
    var before = PerformanceStages.Snapshot();
    var stage = Stopwatch.GetTimestamp();
    var history = await ReadHistoryAsync(ids, ct);
    var historyMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    PerformanceStages.Record("deadhead-read", "history", historyMs);
    stage = Stopwatch.GetTimestamp();
    var saved = await ReadSavedAsync(ids, ct);
    var savedMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    PerformanceStages.Record("deadhead-read", "saved", savedMs);
    stage = Stopwatch.GetTimestamp();
    var rates = await db
      .DispatchRates.AsNoTracking()
      .Where(x => ids.Contains(x.DispatchId))
      .ToDictionaryAsync(x => x.DispatchId, ct);
    var ratesMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    PerformanceStages.Record("deadhead-read", "rates", ratesMs);
    stage = Stopwatch.GetTimestamp();
    var profilesElapsed = 0d;
    var profiles = new Dictionary<Guid, TruckRouteProfile>();
    foreach (var item in items)
    {
      item.EmptyMiles = null;
      item.EmptyMilesStatus = "unavailable";
      item.LoadedRatePerMile = DispatchRates.PerMile(
        item.Price,
        item.LoadedMiles
      );
      item.TotalRatePerMile = null;
      var snapshot = history.GetValueOrDefault(item.Id);
      var load = snapshot?.Current;
      var pair = DeadheadConnection.Find(snapshot);
      if (pair is null)
        continue;
      var truck = load!.TruckId!.Value;
      if (!profiles.TryGetValue(truck, out var profile))
      {
        var profileStarted = Stopwatch.GetTimestamp();
        profiles[truck] = profile = await plans.ProfileAsync(truck, ct);
        profilesElapsed += Stopwatch
          .GetElapsedTime(profileStarted)
          .TotalMilliseconds;
      }
      item.EmptyMilesStatus = "pending";
      if (
        saved.TryGetValue(item.Id, out var entry)
        && entry.InputHash == pair.Signature(profile)
      )
      {
        item.EmptyMiles = entry.Miles;
        item.EmptyMilesStatus = entry.Miles.HasValue ? "ready" : "unavailable";
        if (
          rates.TryGetValue(item.Id, out var rate)
          && DispatchRates.Matches(
            rate,
            RateInputs(load),
            entry.Miles,
            entry.InputHash
          )
          && rate.Price == item.Price
          && rate.LoadedMiles == item.LoadedMiles
          && rate.Currency == item.Currency
        )
        {
          item.LoadedRatePerMile = rate.LoadedRatePerMile;
          item.TotalRatePerMile = rate.TotalRatePerMile;
        }
        else if (entry.Miles is >= 0 && item.LoadedMiles is > 0)
          item.TotalRatePerMile = DispatchRates.PerMile(
            item.Price,
            item.LoadedMiles + entry.Miles
          );
      }
    }
    var matchMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    PerformanceStages.Record("deadhead-read", "match", matchMs);
    PerformanceStages.Record("deadhead-read", "profiles", profilesElapsed);
    PerformanceStages.Count("deadhead-read", "loads", ids.Length);
    PerformanceStages.Count("deadhead-read", "trucks", profiles.Count);

    // This read is the largest single part of a board request that includes
    // financials, so it says what it spent its time on rather than leaving
    // the board's one number to be guessed at.
    var total = historyMs + savedMs + ratesMs + matchMs;
    if (total >= 500)
      logger.LogInformation(
        "DeadheadTiming TotalMs={Total} HistoryMs={History} SavedMs={Saved} "
          + "RatesMs={Rates} MatchMs={Match} ProfilesMs={Profiles} "
          + "Loads={Loads} Trucks={Trucks} Inside={Inside}",
        Math.Round(total),
        Math.Round(historyMs),
        Math.Round(savedMs),
        Math.Round(ratesMs),
        Math.Round(matchMs),
        Math.Round(profilesElapsed),
        ids.Length,
        profiles.Count,
        Inside(before)
      );
  }

  // Totals are cumulative, so this request's share is the difference between
  // the snapshot taken before it and the one taken after. A running total
  // printed as though it were one request reads as a number that grows on
  // its own.
  private static string Inside(
    IReadOnlyDictionary<string, PerformanceStages.StageTiming> before
  ) =>
    string.Join(
      " ",
      PerformanceStages
        .Snapshot()
        .Where(x =>
          x.Key.StartsWith("deadhead-history/", StringComparison.Ordinal)
        )
        .Select(x =>
        {
          var was = before.GetValueOrDefault(x.Key);
          return (
            Name: x.Key["deadhead-history/".Length..],
            Ms: x.Value.TotalMs - (was?.TotalMs ?? 0),
            Calls: x.Value.Count - (was?.Count ?? 0)
          );
        })
        .Where(x => x.Calls > 0)
        .Select(x => $"{x.Name}={Math.Round(x.Ms)}/{x.Calls}")
    );
}
