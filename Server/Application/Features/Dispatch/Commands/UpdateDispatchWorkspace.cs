using System.Data;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Dispatch.Commands.SyncDispatche;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Models.Execution;

namespace Application.Features.Dispatch.Commands;

public sealed record UpdateDispatchWorkspaceCommand(
  Guid DispatchId,
  UpdateDispatchWorkspaceRequest Request
) : IRequest<RequestResponse<DispatchWorkspaceResponse>>;

public sealed class UpdateDispatchWorkspaceHandler(
  IAppDbContext db,
  ICurrentUser caller,
  IUserRoleService roles,
  TimeProvider clock,
  ReadCache reads,
  RoutePreparationQueue preparation
)
  : IRequestHandler<
    UpdateDispatchWorkspaceCommand,
    RequestResponse<DispatchWorkspaceResponse>
  >
{
  public async Task<RequestResponse<DispatchWorkspaceResponse>> Handle(
    UpdateDispatchWorkspaceCommand command,
    CancellationToken ct
  )
  {
    var actor = await ExecutionCommandSupport.ActorAsync(db, caller, roles, ct);
    if (actor is null)
      return Fail("Access denied.", 403);
    var request = command.Request;
    if (
      request is null
      || command.DispatchId == Guid.Empty
      || request.IdempotencyKey == Guid.Empty
      || request.ExpectedRevision < 0
      || request.ExpectedRevision == long.MaxValue
      || request.SourceFingerprint?.Length != 64
    )
      return Fail("Provide the opened revision and request identity.", 400);
    var hash = DispatchWorkspaceData.Hash(command);
    request = DispatchWorkspaceData.Read<UpdateDispatchWorkspaceRequest>(
      DispatchWorkspaceData.Write(request)
    );
    await ProcessGates.Dispatch.WaitAsync(ct);
    try
    {
      await using var transaction = await db.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        ct
      );
      var receipt = await db
        .DispatchWorkspaceRevisions.AsNoTracking()
        .SingleOrDefaultAsync(
          x => x.IdempotencyKey == request.IdempotencyKey,
          ct
        );
      if (receipt is not null)
        return
          receipt.RequestHash == hash
          && receipt.RecordedBy == actor.Value
          && receipt.DispatchId == command.DispatchId
          ? RequestResponse<DispatchWorkspaceResponse>.Ok(
            DispatchWorkspaceData.Read<DispatchWorkspaceResponse>(
              receipt.SnapshotJson
            )
          )
          : Fail("The retry identity belongs to a different save.");
      var state = await DispatchWorkspaceReader.ReadAsync(
        db,
        command.DispatchId,
        true,
        ct
      );
      if (state is null)
        return Fail("Load not found.", 404);
      var current = state.Response;
      if (!current.CanEdit)
        return Fail(current.ReadOnlyReason ?? "This load is read-only.");
      if (
        current.Revision != request.ExpectedRevision
        || current.SourceFingerprint != request.SourceFingerprint
      )
        return Fail("The load changed. Reload before saving your draft.");
      var problem = DispatchWorkspaceRules.Validate(current, request);
      if (problem is not null)
        return Fail(problem, 400);
      if (request.Metadata.BrokerId is { } brokerId)
      {
        var brokerName = await db
          .Customers.AsNoTracking()
          .Where(x => x.Id == brokerId)
          .Select(x => x.NormalizedName)
          .SingleOrDefaultAsync(ct);
        if (
          brokerName is null
          || brokerName
            != CustomerMatcher.Normalize(request.Metadata.BrokerCompany)
        )
          return Fail("Select the broker that matches this load.", 400);
      }
      var driverIds = request
        .Metadata.Adjustments.Where(x => x.Target == "driver")
        .Select(x => x.DriverId!.Value)
        .Distinct()
        .ToArray();
      var driverNames = await db
        .Drivers.AsNoTracking()
        .Where(x => driverIds.Contains(x.Id))
        .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
      if (driverIds.Any(id => !driverNames.ContainsKey(id)))
        return Fail("Select an existing driver for each adjustment.", 400);
      foreach (var row in request.Metadata.Adjustments)
        row.DriverName = row.DriverId is { } driverId
          ? current
            .Metadata.Adjustments.FirstOrDefault(old =>
              old.Id == row.Id && old.DriverId == driverId
            )
            ?.DriverName ?? driverNames[driverId]
          : "";
      var existingIds = current.Stops.Select(x => x.Id).ToHashSet();
      var addedIds = request
        .Stops.Where(x => !existingIds.Contains(x.Id))
        .Select(x => x.Id)
        .ToArray();
      if (
        await db.DispatchStops.AnyAsync(x => addedIds.Contains(x.Id), ct)
        || await db.ExecutionLegStops.AnyAsync(x => addedIds.Contains(x.Id), ct)
      )
        return Fail("A new stop identity is already in use.");
      var load = state.Load;
      var workspace = state.Workspace;
      var now = clock.GetUtcNow().UtcDateTime;
      if (workspace is null)
      {
        workspace = new DispatchWorkspace { Id = load.Id };
        db.DispatchWorkspaces.Add(workspace);
      }
      var changedStops =
        !current
          .Stops.Select(x => x.Id)
          .SequenceEqual(request.Stops.Select(x => x.Id))
        || request.Stops.Any(x =>
          current.Stops.SingleOrDefault(s => s.Id == x.Id) is not { } before
          || DispatchWorkspaceData.OperationalHash(before)
            != DispatchWorkspaceData.OperationalHash(x)
        );
      if (changedStops)
      {
        if (!workspace.OwnsStops)
          workspace.SourceStopsJson = ExecutionSnapshots.Write(load.Stops);
        workspace.OwnsStops = true;
        await ApplyStopsAsync(state, request, actor.Value, now, ct);
      }
      if (
        DispatchWorkspaceData.Hash(DispatchWorkspaceData.Commercial(load))
        != DispatchWorkspaceData.Hash(
          DispatchWorkspaceData.Commercial(request.Metadata)
        )
      )
      {
        if (!workspace.OwnsCommercial)
          workspace.SourceCommercialJson = DispatchWorkspaceData.Write(
            DispatchWorkspaceData.Commercial(load)
          );
        workspace.OwnsCommercial = true;
        DispatchWorkspaceData.ApplyCommercial(load, request.Metadata);
      }
      workspace.MetadataJson = DispatchWorkspaceData.Write(request.Metadata);
      workspace.StopExtrasJson = DispatchWorkspaceData.Write(
        request.Stops.ToDictionary(x => x.Id)
      );
      workspace.Revision++;
      workspace.RecordedAt = now;
      workspace.RecordedBy = actor.Value;
      var history = new DispatchWorkspaceRevision
      {
        Id = Guid.NewGuid(),
        DispatchId = load.Id,
        Revision = workspace.Revision,
        IdempotencyKey = request.IdempotencyKey,
        RequestHash = hash,
        RecordedAt = now,
        RecordedBy = actor.Value,
        ActorName = await db
          .Users.Where(x => x.Id == actor.Value)
          .Select(x => x.Name)
          .SingleAsync(ct),
        Summary = changedStops
          ? "Updated load and future stops."
          : "Updated load information.",
      };
      db.DispatchWorkspaceRevisions.Add(history);
      await db.SaveChangesAsync(ct);
      var saved = await DispatchWorkspaceReader.ReadAsync(
        db,
        load.Id,
        true,
        ct
      );
      history.SnapshotJson = DispatchWorkspaceData.Write(saved!.Response);
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      foreach (
        var key in new[] { "dispatch", "board", "execution", "route-previews" }
      )
        reads.Invalidate(key);
      reads.Invalidate($"route:{load.Id}");
      preparation.MarkDirty(load.Id);
      foreach (var leg in state.Legs)
      {
        reads.Invalidate($"route:{load.Id}:leg:{leg.Id}");
        preparation.MarkTruckDirty(leg.TruckId);
      }
      foreach (
        var truck in load
          .Stops.Select(x => x.TruckId)
          .Prepend(load.PlanningTruckId ?? load.TruckId)
          .Where(x => x.HasValue)
          .Select(x => x!.Value)
          .Distinct()
      )
        preparation.MarkTruckDirty(truck);
      return RequestResponse<DispatchWorkspaceResponse>.Ok(saved.Response);
    }
    catch (Exception ex) when (db.IsWriteConflict(ex))
    {
      return Fail("The load changed concurrently. Retry or reload the draft.");
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private async Task ApplyStopsAsync(
    DispatchWorkspaceState state,
    UpdateDispatchWorkspaceRequest request,
    Guid actor,
    DateTime now,
    CancellationToken ct
  )
  {
    var load = state.Load;
    var ids = request.Stops.Select(x => x.Id).ToHashSet();
    var removed = state
      .Response.Stops.Where(x => !ids.Contains(x.Id))
      .Select(x => x.Id)
      .ToHashSet();
    foreach (
      var stop in load.Stops.Where(x => removed.Contains(x.Id)).ToArray()
    )
    {
      load.Stops.Remove(stop);
      db.DispatchStops.Remove(stop);
    }
    var effective = new Dictionary<Guid, DispatchStop>();
    foreach (var (value, index) in request.Stops.Select((x, i) => (x, i)))
    {
      var existed = state.EffectiveStops.TryGetValue(
        value.Id,
        out var previous
      );
      var stop = existed
        ? ExecutionSnapshots.Copy(previous!)
        : new DispatchStop { Id = value.Id, DispatchId = load.Id };
      DispatchWorkspaceData.Apply(stop, value);
      stop.Sequence = index + 1;
      if (!existed)
      {
        var anchor = state.Response.Stops.First(x =>
          x.SegmentKey == value.SegmentKey && x.CanMove
        );
        var resource = state.EffectiveStops[anchor.Id];
        stop.TruckId = resource.TruckId;
        stop.DriverId = resource.DriverId;
        stop.CoDriverId = resource.CoDriverId;
        stop.TrailerId = resource.TrailerId;
        stop.TruckNumber = resource.TruckNumber;
        stop.DriverName = resource.DriverName;
        stop.CoDriverName = resource.CoDriverName;
        stop.TrailerNumber = resource.TrailerNumber;
        stop.SourceAddressJson = StopAddress.From(stop).Serialize();
        stop.AddressVerifiedAt = now;
        load.Stops.Add(stop);
        db.DispatchStops.Add(stop);
      }
      else if (load.Stops.SingleOrDefault(x => x.Id == stop.Id) is { } source)
      {
        var changedAddress =
          source.Address != stop.Address
          || source.City != stop.City
          || source.Province != stop.Province
          || source.Country != stop.Country
          || source.ZipCode != stop.ZipCode
          || source.Latitude != stop.Latitude
          || source.Longitude != stop.Longitude;
        DispatchWorkspaceData.Apply(source, value);
        source.Sequence = stop.Sequence;
        if (changedAddress)
        {
          source.SourceAddressJson = StopAddress.From(source).Serialize();
          source.AddressVerifiedAt = now;
          source.AddressRetryAfter = null;
          stop.SourceAddressJson = source.SourceAddressJson;
          stop.AddressVerifiedAt = now;
          stop.AddressRetryAfter = null;
        }
      }
      effective.Add(stop.Id, stop);
    }
    var changes = new List<ExecutionChange>();
    foreach (var leg in state.Legs)
    {
      var planned = request
        .Stops.Where(x => x.ExecutionLegId == leg.Id)
        .Select(
          (value, position) =>
          {
            var stop = ExecutionSnapshots.Copy(effective[value.Id]);
            stop.Sequence = position + 1;
            return stop;
          }
        )
        .ToList();
      var snapshot = ExecutionSnapshots.Write(planned);
      if (snapshot == ExecutionSnapshots.Write(ExecutionStopRows.Read(leg)))
        continue;
      changes.Add(new(leg, planned) { UpdateBoundaries = true });
    }
    await ExecutionAcceptance.ApplyAsync(
      db,
      changes,
      "workspace-updated",
      actor,
      request.IdempotencyKey,
      now,
      ct
    );
  }

  private static RequestResponse<DispatchWorkspaceResponse> Fail(
    string message,
    int status = 409
  ) => RequestResponse<DispatchWorkspaceResponse>.Fail(message, status);
}
