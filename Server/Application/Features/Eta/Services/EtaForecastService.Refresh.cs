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
    var committed = false;
    EtaForecastSnapshot[] snapshots = [];
    var changed = false;
    // Why a forecast was not saved: truck 11006's forecast failed to save
    // every few minutes and the log could not say which of these it was.
    var unsaved = "store-refused";
    // Readers see a forecast only once it is saved. It used to be published
    // first and taken back when the save failed, so the map's ETA went blank
    // whenever one refresh could not save, even for a passing reason
    // (11006, September 26). A new result is now published only after its
    // commit. The one readers already have stays unless the failure
    // demonstrates that its inputs changed - the chain was described
    // otherwise, or a check that compares a captured version with the
    // current one said so - because then it was calculated on inputs that
    // no longer hold, and readers, who compare only the work and the road,
    // would show it as current. A failure that proves nothing about the
    // inputs (the store, the network, planning busy) leaves it to the
    // readers' own guards. It is retired only if still the one read here: a
    // newer one another refresh published meanwhile stays.
    EtaService.Calculation? calculation = null;
    var shown = memory.Results.GetValueOrDefault(rootKey);
    EtaMemory.Entry? waiting = null;
    try
    {
      calculation = await eta.CalculateAsync(
        state,
        ct,
        viewed: false,
        chain,
        publish: false
      );
      var result = calculation?.Value;
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
        waiting = new(description.InputHash, result)
        {
          ChainInputHash = description.InputHash,
        };
      }
      var current = await inputs.DescribeAsync(truckId, ct);
      if (current?.InputHash != description.InputHash)
      {
        changed = true;
        unsaved = "inputs-changed";
        return;
      }
      // The root's row keeps the whole chain's forecast - what this
      // process' memory holds and the map shows - with the keys of the plan
      // it was calculated on, so another process reads the same answer;
      // readers of one load take their part (PopulateAsync). A waiting
      // result has no keys here as it has none in memory.
      var keys = calculation is null
        ? ((string, string)?)null
        : eta.SavedKeys(state);
      snapshots = description
        .Loads.Select(load => (Load: load, Root: IsRoot(load, description)))
        .Select(x => new EtaForecastSnapshot(
          x.Load.Id,
          truckId,
          description.RootDispatchId,
          description.InputHash,
          description.DriverExternalId,
          x.Root ? result : Filter(result, x.Load.Id)
        )
        {
          ExecutionLegId = x.Load.ExecutionLegId,
          RootExecutionLegId = description.RootExecutionLegId,
          AssignmentRevision = x.Load.AssignmentRevision,
          WorkKey = x.Root ? keys?.Item1 : null,
          RouteKey = x.Root ? keys?.Item2 : null,
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
        RoutePlanningException { DependencyChanged: true } =>
          "dependency-changed",
        RoutePlanningException { Busy: true } => "planning-busy",
        OperationCanceledException => "cancelled",
        RoutePlanningException => "refused",
        _ => "failed",
      };
      throw;
    }
    finally
    {
      if (committed)
      {
        Committed(snapshots);
        if (calculation is { Published: false })
          eta.Publish(state, calculation, description.InputHash);
        else if (waiting is not null)
          memory.Publish(rootKey, waiting);
      }
      else
      {
        PerformanceStages.Count("eta-memory", "unsaved", 1);
        PerformanceStages.Count("eta-memory", $"unsaved-{unsaved}", 1);
        var retire =
          shown is not null
          && unsaved is ("inputs-changed" or "dependency-changed")
          && memory.RemoveIfCurrent(rootKey, shown);
        logger.LogInformation(
          "ETA forecast for scope {EtaScope} was not saved and not "
            + "published: {EtaUnsavedReason}; the shown one {EtaShown}",
          rootKey,
          unsaved,
          retire ? "was retired" : "stays"
        );
        if (changed)
          memory.RequestRefresh();
      }
    }
  }

  private static bool IsRoot(
    RouteWorkSnapshot load,
    EtaChainDescription chain
  ) =>
    load.Id == chain.RootDispatchId
    && load.ExecutionLegId == chain.RootExecutionLegId;

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
