using System.Diagnostics;
using Application.Diagnostics;
using Application.Features.Dispatch.Models;
using Application.Features.Eta.Interfaces;
using Application.Features.Routing.Services.Routes;
using Domain.Models.Eta;
using Domain.Models.Routing;
using Domain.Rules;
using Domain.Rules.Routing;
using Microsoft.Extensions.Logging;

namespace Application.Features.Eta.Services;

// Rebuilding one truck's forecast on request: the saved one is discarded,
// the chain is described again from what is current, and the result is
// published under the same reservation the rest of planning uses.
public sealed partial class EtaForecastService
{
  public async Task RefreshAsync(Guid requestedDispatchId, CancellationToken ct)
  {
    var identity = memory.Resolve(requestedDispatchId);
    RouteWorkSnapshot? requested;
    try
    {
      requested = await routes.TryLoadAsync(
        identity.DispatchId,
        ct,
        identity.ExecutionLegId
      );
    }
    catch (RoutePlanningException) when (identity.ExecutionLegId.HasValue)
    {
      Drop(requestedDispatchId, "the leg could not be loaded");
      return;
    }
    // The queue does not know whose load it holds, so the refresh is offered
    // to every carrier in turn (EtaRefreshOperation). A carrier that does not
    // have the load leaves the owner's forecast alone: dropping it here
    // emptied the map's ETA every half minute once a second carrier existed
    // (trucks 11005, 11007 and 54777, September 25).
    if (requested is null)
      return;
    if (!PlanningWorkPolicy.CanUseGps(requested))
    {
      Drop(requestedDispatchId, "the load cannot use GPS");
      return;
    }
    if (requested.TruckId is not { } truckId)
      return;
    var description = await inputs.DescribeAsync(truckId, ct);
    if (description is null)
    {
      Drop(requestedDispatchId, "the truck has no chain to describe");
      return;
    }
    var rootKey = memory.Scope(
      description.RootDispatchId,
      description.RootExecutionLegId
    );
    if (rootKey != requestedDispatchId)
    {
      Drop(requestedDispatchId, "the chain's root is another scope");
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
    // Why a published forecast was not saved, for the line that takes it
    // back: truck 11006's forecast was taken back every few minutes and
    // the log could not say which of these it was.
    var unsaved = "store-refused";
    try
    {
      var result = await eta.GetAsync(state, ct, viewed: false, chain);
      if (result is null)
      {
        var now = DateTime.UtcNow;
        result = new(
          now,
          now + EtaMemory.RefreshInterval,
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
        unsaved = "inputs-changed";
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
    catch (Exception failure)
    {
      unsaved = failure switch
      {
        RoutePlanningException { Busy: true } => "planning-busy",
        OperationCanceledException => "cancelled",
        _ => "failed",
      };
      throw;
    }
    finally
    {
      if (
        !committed
        && published is not null
        && memory.RemoveIfCurrent(rootKey, published)
      )
      {
        // Published to readers, then not saved: taken back.
        PerformanceStages.Count("eta-memory", "unsaved-removed", 1);
        PerformanceStages.Count("eta-memory", $"unsaved-{unsaved}", 1);
        logger.LogInformation(
          "ETA forecast for scope {EtaScope} was not saved and was taken "
            + "back: {EtaUnsavedReason}",
          rootKey,
          unsaved
        );
        if (changed)
          memory.RequestRefresh();
      }
    }
  }

  // A forecast readers could see, dropped by a refresh: said once with why
  // (the open map ETA incident, where trucks lost theirs every half minute).
  private void Drop(Guid scope, string reason)
  {
    if (memory.Forget(scope))
      logger.LogInformation(
        "ETA memory dropped the forecast for scope {EtaScope}: {EtaReason}",
        scope,
        reason
      );
  }
}
