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
    var changed = false;
    // Why a forecast was not saved: truck 11006's forecast failed to save
    // every few minutes and the log could not say which of these it was.
    var unsaved = "store-refused";
    // Readers see a forecast only once it is saved. It used to be published
    // first and taken back when the save failed, so the map's ETA went blank
    // whenever one refresh could not save, even for a passing reason
    // (11006, September 26). A new result is now published only after its
    // commit. The one readers already have stays, unless the failure says
    // its inputs changed: then it was calculated on inputs that no longer
    // hold, and readers, who compare only the work and the road, would show
    // it as current. It is retired only if still the one read here: a newer
    // one another refresh published meanwhile stays.
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
        RoutePlanningException => "dependency-changed",
        _ => "failed",
      };
      throw;
    }
    finally
    {
      if (committed)
      {
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
          && unsaved is not ("planning-busy" or "cancelled")
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
