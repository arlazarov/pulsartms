using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Entities.Fleet;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public partial class SyncDispatchesCommandHandler
{
  // Everything the import has already read for the whole page, handed to
  // each source load in one piece: the reference data it is matched
  // against, the loads and links it may already have, and the two sets
  // recording what this run has to re-plan afterwards.
  private sealed record SyncScope(
    string ProviderKey,
    IDispatchProvider Provider,
    Dictionary<string, Customer> Customers,
    DriverMatcher.Index DriverIndex,
    Dictionary<string, Truck> Trucks,
    Dictionary<string, Trailer> Trailers,
    Dictionary<string, DispatchEntity> Dispatches,
    Dictionary<string, int> Reserved,
    List<DispatchSourceLink> Links,
    Dictionary<Guid, DispatchWorkspace> Workspaces,
    HashSet<Guid> Dirty,
    HashSet<Guid> AffectedTrucks,
    DateTime SyncedAt
  );

  // One source load against what the database already holds for it.
  private void Apply(ExternalDispatch source, SyncScope scope)
  {
    var (
      providerKey,
      dispatchProvider,
      customers,
      driverIndex,
      trucks,
      trailers,
      dispatches,
      reserved,
      links,
      workspaces,
      dirty,
      affectedTrucks,
      syncedAt
    ) = scope;
    var customerName = CustomerMatcher.Normalize(source.CustomerName);
    var customer = string.IsNullOrEmpty(customerName)
      ? null
      : customers.GetValueOrDefault(customerName);

    if (customer is null && !string.IsNullOrWhiteSpace(source.CustomerName))
    {
      customer = CustomerMatcher.Create(source.CustomerName);
      customers[customer.NormalizedName] = customer;
      dbContext.Customers.Add(customer);
    }

    var driver = driverIndex.Match(source.DriverName);
    var truck = trucks.GetValueOrDefault(source.TruckNumber);
    var trailer = trailers.GetValueOrDefault(source.TrailerNumber);
    var dispatch = dispatches.GetValueOrDefault(source.ExternalId);

    if (dispatch is null)
    {
      dispatch = new DispatchEntity
      {
        Id = Guid.NewGuid(),
        LoadNumber = reserved[source.ExternalId],
      };

      dbContext.Dispatches.Add(dispatch);
      dispatches.Add(source.ExternalId, dispatch);
      var sourceLink = new DispatchSourceLink
      {
        Provider = providerKey,
        ExternalId = source.ExternalId,
        DisplayName = dispatchProvider.DisplayName,
        Dispatch = dispatch,
        DispatchId = dispatch.Id,
      };
      links.Add(sourceLink);
      dbContext.DispatchSourceLinks.Add(sourceLink);
    }
    var provenance = links.Single(x => x.DispatchId == dispatch.Id);
    var sourceAssignment = DispatchSourceAssignments.Capture(
      source,
      value => trucks.GetValueOrDefault(value)?.Id,
      value => driverIndex.Match(value)?.Id,
      value => trailers.GetValueOrDefault(value)?.Id
    );

    provenance.AssignmentSignature = DispatchSourceAssignments.Fingerprint(
      sourceAssignment
    );
    provenance.AssignmentProposalJson = DispatchWorkspaceData.Write(
      sourceAssignment
    );
    var previousTrucks = dispatch
      .Stops.Select(x => x.TruckId)
      .Prepend(dispatch.TruckId)
      .Prepend(dispatch.PlanningTruckId)
      .Where(x => x.HasValue)
      .Select(x => x!.Value)
      .ToArray();
    var previousInputs = RoutePreparationInputs.Signature(
      dispatch,
      RoutePreparationInputs.Truck(dispatch),
      0,
      reads,
      syncedAt
    );
    DispatchMapper.Update(
      dispatch,
      source,
      customer,
      driver,
      truck,
      trailer,
      syncedAt
    );

    if (workspaces.TryGetValue(dispatch.Id, out var workspace))
      DispatchWorkspaceImport.RestoreCommercial(dispatch, workspace);

    var existingStops = dispatch.Stops.ToList();
    if (workspace?.OwnsStops == true)
    {
      DispatchWorkspaceImport.MergeStops(
        dispatch,
        workspace,
        source.Stops,
        syncedAt
      );
      dispatch.LastSyncedAt = syncedAt;
      if (
        previousInputs
        != RoutePreparationInputs.Signature(
          dispatch,
          RoutePreparationInputs.Truck(dispatch),
          0,
          reads,
          syncedAt
        )
      )
      {
        dirty.Add(dispatch.Id);
        affectedTrucks.UnionWith(previousTrucks);
      }
      return;
    }
    var stopMatches = DispatchStopMatcher.Match(existingStops, source.Stops);
    var retainedIds = stopMatches.Values.Select(x => x.Id).ToHashSet();
    foreach (
      var removed in existingStops.Where(x => !retainedIds.Contains(x.Id))
    )
    {
      dbContext.DispatchStops.Remove(removed);
      dispatch.Stops.Remove(removed);
    }
    var stopsChanged = DispatchComparer.StopsChanged(
      existingStops,
      source.Stops
    );
    foreach (var sourceStop in source.Stops.OrderBy(x => x.Sequence))
    {
      var stop = stopMatches.GetValueOrDefault(sourceStop);
      var stopDriver = driverIndex.Match(sourceStop.DriverName);
      var coDriver = driverIndex.Match(sourceStop.CoDriverName);
      var stopTruck = trucks.GetValueOrDefault(sourceStop.TruckNumber);
      var stopTrailer = trailers.GetValueOrDefault(sourceStop.TrailerNumber);
      if (stop is null)
      {
        stop = DispatchMapper.CreateStop(
          sourceStop,
          stopDriver,
          coDriver,
          stopTruck,
          stopTrailer
        );
        dispatch.Stops.Add(stop);
        // Assigned stop IDs must still be inserted when the parent load
        // already exists.
        dbContext.DispatchStops.Add(stop);
      }
      else
        DispatchMapper.UpdateStop(
          stop,
          sourceStop,
          stopDriver,
          coDriver,
          stopTruck,
          stopTrailer
        );
    }
    var assignmentReleased =
      DispatchAssignmentReconciliation.ReleaseStaleConfirmation(
        dispatch,
        trucks,
        syncedAt
      );
    if (
      dbContext.Entry(dispatch).State
        is EntityState.Added
          or EntityState.Modified
      || stopsChanged
      || assignmentReleased
    )
      dispatch.LastSyncedAt = syncedAt;
    if (
      assignmentReleased
      || previousInputs
        != RoutePreparationInputs.Signature(
          dispatch,
          RoutePreparationInputs.Truck(dispatch),
          0,
          reads,
          syncedAt
        )
    )
    {
      dirty.Add(dispatch.Id);
      affectedTrucks.UnionWith(previousTrucks);
      affectedTrucks.UnionWith(
        dispatch
          .Stops.Select(x => x.TruckId)
          .Prepend(dispatch.TruckId)
          .Where(x => x.HasValue)
          .Select(x => x!.Value)
      );
    }
  }
}
