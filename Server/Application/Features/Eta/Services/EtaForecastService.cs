using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Dispatch.Models;
using Application.Features.Eta.Interfaces;
using Application.Features.Eta.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Microsoft.Extensions.Logging;

namespace Application.Features.Eta.Services;

public sealed class EtaForecastService(
  EtaChainInputsService inputs,
  IEtaForecastStore store,
  EtaMemory memory,
  EtaService eta,
  RoutePlanningService routes,
  PlanningWorkPublication publication,
  ILogger<EtaForecastService> logger
)
{
  public async Task PopulateAsync(
    IReadOnlyCollection<DispatchResponse> dispatches,
    CancellationToken ct,
    IReadOnlyList<TruckDispatchBoardResponse>? rows = null
  )
  {
    if (rows is not null)
      foreach (var row in rows)
        row.CurrentCycle = null;
    var before = PerformanceStages.Snapshot();
    var stage = Stopwatch.GetTimestamp();
    var saved = await ReadSavedAsync(
      dispatches
        .Where(x => !x.ExecutionLegId.HasValue)
        .Select(x => x.Id)
        .Distinct()
        .ToArray(),
      dispatches
        .Where(x => x.ExecutionLegId.HasValue)
        .Select(x => x.ExecutionLegId!.Value)
        .Distinct()
        .ToArray(),
      ct
    );
    var savedMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    stage = Stopwatch.GetTimestamp();
    var groups = rows is not null
      ? rows.Where(x => x.TruckId.HasValue)
        .Select(x =>
          (
            TruckId: x.TruckId!.Value,
            Loads: (IReadOnlyList<DispatchResponse>)x.Dispatches
          )
        )
        .ToArray()
      : dispatches
        .GroupBy(x =>
          x.TruckId
          ?? x.Stops.Select(s => s.TruckId).FirstOrDefault(id => id.HasValue)
        )
        .Where(x => x.Key.HasValue)
        .Select(x =>
          (
            TruckId: x.Key!.Value,
            Loads: (IReadOnlyList<DispatchResponse>)x.ToArray()
          )
        )
        .ToArray();
    var groupMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    stage = Stopwatch.GetTimestamp();
    var descriptions = rows is null
      ? null
      : await inputs.DescribeManyAsync(rows, ct);
    var describeMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    stage = Stopwatch.GetTimestamp();
    var describedOne = 0d;
    foreach (var group in groups)
    {
      EtaChainDescription? description;
      if (descriptions is null)
      {
        var one = Stopwatch.GetTimestamp();
        description = await inputs.DescribeAsync(group.TruckId, ct);
        describedOne += Stopwatch.GetElapsedTime(one).TotalMilliseconds;
      }
      else
        description = descriptions.GetValueOrDefault(group.TruckId);
      if (description is null)
        continue;
      var now = DateTime.UtcNow;
      var key = memory.Scope(
        description.RootDispatchId,
        description.RootExecutionLegId
      );
      memory.Demand(key, description.InputHash, now);
      if (rows is not null)
      {
        var currentCycle = ReadCurrentCycle(
          saved.GetValueOrDefault(
            (description.RootDispatchId, description.RootExecutionLegId)
          ),
          description,
          now
        );
        foreach (var row in rows.Where(x => x.TruckId == group.TruckId))
          row.CurrentCycle = currentCycle;
      }
      foreach (var load in group.Loads)
      {
        if (
          !description.Loads.Any(x =>
            x.Id == load.Id && x.ExecutionLegId == load.ExecutionLegId
          )
        )
          continue;
        var snapshot = saved.GetValueOrDefault((load.Id, load.ExecutionLegId));
        var matches =
          Matches(snapshot, description)
          && (
            !load.ExecutionLegId.HasValue
            || snapshot!.AssignmentRevision == load.AssignmentRevision
          );
        if (matches)
        {
          load.Eta =
            snapshot!.Forecast.ValidUntil > now
              ? snapshot.Forecast
              : snapshot.Forecast with
              {
                RouteUpdatePending = true,
              };
          if (snapshot.Forecast.ValidUntil > now)
            continue;
        }
        else
          load.Eta = new(
            now,
            now,
            [],
            "ETA unavailable: forecast updating.",
            []
          )
          {
            RouteUpdatePending = true,
          };
      }
    }

    // The forecast is the largest stage of a board request that includes it,
    // and it is one call to here. Saying which part it was beats guessing
    // from the one number the board reports.
    var matchMs = Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
    var total = savedMs + groupMs + describeMs + matchMs;
    if (total >= 500)
      logger.LogInformation(
        "EtaTiming TotalMs={Total} SavedMs={Saved} GroupMs={Group} "
          + "DescribeMs={Describe} DescribeOneMs={DescribeOne} "
          + "MatchMs={Match} Loads={Loads} Trucks={Trucks} Rows={Rows} "
          + "Inside={Inside}",
        Math.Round(total),
        Math.Round(savedMs),
        Math.Round(groupMs),
        Math.Round(describeMs),
        Math.Round(describedOne),
        Math.Round(matchMs),
        dispatches.Count,
        groups.Length,
        rows?.Count ?? 0,
        string.Join(
          " ",
          PerformanceStages
            .Snapshot()
            .Where(x =>
              x.Key.StartsWith("eta-describe/", StringComparison.Ordinal)
              || x.Key.StartsWith("itinerary-read/", StringComparison.Ordinal)
            )
            .Select(x =>
            {
              var was = before.GetValueOrDefault(x.Key);
              return (
                Name: x.Key[(x.Key.IndexOf('/') + 1)..],
                Ms: x.Value.TotalMs - (was?.TotalMs ?? 0),
                Calls: x.Value.Count - (was?.Count ?? 0)
              );
            })
            .Where(x => x.Calls > 0)
            .Select(x => $"{x.Name}={Math.Round(x.Ms)}/{x.Calls}")
        )
      );
  }

  private static bool Matches(
    EtaForecastSnapshot? snapshot,
    EtaChainDescription description
  ) =>
    snapshot is not null
    && snapshot.TruckId == description.TruckId
    && snapshot.RootDispatchId == description.RootDispatchId
    && snapshot.RootExecutionLegId == description.RootExecutionLegId
    && snapshot.InputHash == description.InputHash
    && snapshot.DriverExternalId == description.DriverExternalId;

  private static DriverCycleSnapshot? ReadCurrentCycle(
    EtaForecastSnapshot? snapshot,
    EtaChainDescription description,
    DateTime now
  )
  {
    if (
      !Matches(snapshot, description)
      || snapshot!.DispatchId != description.RootDispatchId
      || snapshot.ExecutionLegId != description.RootExecutionLegId
      || description.RootExecutionLegId.HasValue
        && snapshot.AssignmentRevision
          != description.Loads[0].AssignmentRevision
      || string.IsNullOrWhiteSpace(description.DriverExternalId)
    )
      return null;
    var forecast = snapshot.Forecast;
    if (
      forecast.CalculatedAt > now
      || forecast.ValidUntil <= forecast.CalculatedAt
      || forecast.ValidUntil <= now.AddMinutes(-15)
      || forecast.CycleAtCalculation
        is not {
          RecapVerified: true,
          NextRecapMinutes: > 0,
          NextRecapAt: { } recap
        } cycle
      || recap <= now
      || string.IsNullOrWhiteSpace(cycle.HomeTimeZoneId)
    )
      return null;
    return new(forecast.CalculatedAt, forecast.ValidUntil, cycle);
  }

  public async Task RefreshAsync(Guid requestedDispatchId, CancellationToken ct)
  {
    var identity = memory.Resolve(requestedDispatchId);
    RouteWorkSnapshot requested;
    try
    {
      requested = await routes.LoadAsync(
        identity.DispatchId,
        ct,
        identity.ExecutionLegId
      );
    }
    catch (RoutePlanningException) when (identity.ExecutionLegId.HasValue)
    {
      memory.Forget(requestedDispatchId);
      return;
    }
    if (!PlanningWorkPolicy.CanUseGps(requested))
    {
      memory.Forget(requestedDispatchId);
      return;
    }
    if (requested.TruckId is not { } truckId)
      return;
    var description = await inputs.DescribeAsync(truckId, ct);
    if (description is null)
    {
      memory.Forget(requestedDispatchId);
      return;
    }
    var rootKey = memory.Scope(
      description.RootDispatchId,
      description.RootExecutionLegId
    );
    if (rootKey != requestedDispatchId)
    {
      memory.Forget(requestedDispatchId);
      memory.View(rootKey, DateTime.UtcNow);
    }
    var chain = await inputs.PrepareAsync(description, ct);
    var state = await routes.GetAsync(
      description.Loads[0],
      ct,
      cachedTelemetryOnly: true
    );
    EtaMemory.Entry? published = null;
    var committed = false;
    var changed = false;
    try
    {
      var result = await eta.GetAsync(state, ct, viewed: false, chain);
      if (result is null)
      {
        var now = DateTime.UtcNow;
        result = new(
          now,
          now.AddMinutes(2),
          [],
          "ETA unavailable: waiting for the current saved route.",
          []
        )
        {
          RouteUpdatePending = true,
        };
        memory.Results[rootKey] = new(description.InputHash, result)
        {
          ChainInputHash = description.InputHash,
        };
      }
      if (
        memory.Results.TryGetValue(rootKey, out var entry)
        && ReferenceEquals(entry.Value, result)
      )
        published = entry;
      var current = await inputs.DescribeAsync(truckId, ct);
      if (current?.InputHash != description.InputHash)
      {
        changed = true;
        return;
      }
      var snapshots = description
        .Loads.Select(load => new EtaForecastSnapshot(
          load.Id,
          truckId,
          description.RootDispatchId,
          description.InputHash,
          description.DriverExternalId,
          Filter(result, load.Id)
        )
        {
          ExecutionLegId = load.ExecutionLegId,
          RootExecutionLegId = description.RootExecutionLegId,
          AssignmentRevision = load.AssignmentRevision,
        })
        .ToArray();
      await using (
        var transaction = await publication.BeginAsync(
          description.Itinerary,
          [description.History],
          ct
        )
      )
      {
        await inputs.RequireCurrentProfileAsync(description, ct);
        await inputs.RequireCurrentRoadsAsync(description, state.Plan, ct);
        var saved = await store.SaveAsync(snapshots, ct);
        await transaction.CommitAsync(ct);
        committed = saved;
      }
      if (!committed)
      {
        var saved = await ReadSavedAsync(
          snapshots
            .Where(x => !x.ExecutionLegId.HasValue)
            .Select(x => x.DispatchId)
            .ToArray(),
          snapshots
            .Where(x => x.ExecutionLegId.HasValue)
            .Select(x => x.ExecutionLegId!.Value)
            .ToArray(),
          ct
        );
        committed = snapshots.All(snapshot =>
          saved.TryGetValue(
            (snapshot.DispatchId, snapshot.ExecutionLegId),
            out var existing
          )
          && existing.RootDispatchId == snapshot.RootDispatchId
          && existing.RootExecutionLegId == snapshot.RootExecutionLegId
          && existing.AssignmentRevision == snapshot.AssignmentRevision
          && existing.TruckId == snapshot.TruckId
          && existing.InputHash == snapshot.InputHash
          && existing.DriverExternalId == snapshot.DriverExternalId
          && existing.Forecast.CalculatedAt == snapshot.Forecast.CalculatedAt
          && existing.Forecast.ValidUntil == snapshot.Forecast.ValidUntil
        );
      }
    }
    finally
    {
      if (
        !committed
        && published is not null
        && memory.RemoveIfCurrent(rootKey, published)
        && changed
      )
        memory.RequestRefresh();
    }
  }

  private async Task<
    Dictionary<(Guid, Guid?), EtaForecastSnapshot>
  > ReadSavedAsync(Guid[] dispatchIds, Guid[] legIds, CancellationToken ct)
  {
    List<EtaForecastSnapshot> snapshots = [];
    if (dispatchIds.Length > 0)
      snapshots.AddRange(await store.ReadAsync(dispatchIds, ct));
    if (legIds.Length > 0)
      snapshots.AddRange(await store.ReadExecutionLegsAsync(legIds, ct));
    return snapshots.ToDictionary(x => (x.DispatchId, x.ExecutionLegId));
  }

  public static DispatchEta Filter(DispatchEta forecast, Guid dispatchId)
  {
    var stops = forecast.Stops.Where(x => x.DispatchId == dispatchId).ToArray();
    var reason =
      stops.Length == 0
        ? forecast.PendingDispatches.GetValueOrDefault(dispatchId)
          ?? forecast.UnavailableReason
        : null;
    return forecast with
    {
      Stops = stops,
      UnavailableReason = reason,
      RouteUpdatePending = forecast.RouteUpdatePending || reason is not null,
      PendingDispatches = new Dictionary<Guid, string>(),
    };
  }
}
