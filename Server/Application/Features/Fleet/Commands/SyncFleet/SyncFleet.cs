using Application.Caching;
using Application.Features.Synchronization.Services;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Commands.SyncFleet;

public record SyncFleetCommand : IRequest<RequestResponse<int>>;

public class SyncFleetHandler(
  IAppDbContext dbContext,
  IFleetProvider fleetProvider,
  IMemoryCache cache, ReadCache reads
) : IRequestHandler<SyncFleetCommand, RequestResponse<int>>
{
  public async Task<RequestResponse<int>> Handle(
    SyncFleetCommand request,
    CancellationToken cancellationToken
  )
  {
    await SynchronizationGates.Fleet.WaitAsync(cancellationToken);
    try { return await SyncAsync(request, cancellationToken); }
    finally { SynchronizationGates.Fleet.Release(); }
  }

  private async Task<RequestResponse<int>> SyncAsync(
    SyncFleetCommand request,
    CancellationToken cancellationToken
  )
  {
    var drivers = await fleetProvider.GetDriversAsync(cancellationToken);
    var trucks = await fleetProvider.GetVehiclesAsync(cancellationToken);
    var trailers = await fleetProvider.GetTrailersAsync(cancellationToken);
    var snapshotTime = DateTime.UtcNow;
    var assignments = await fleetProvider.GetAssignmentsAsync(
      snapshotTime, snapshotTime, cancellationToken);
    var trailerAssignments = await fleetProvider.GetTrailerAssignmentsAsync(
      drivers.Select(x => x.ExternalId).ToArray(), cancellationToken);

    await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

    await DriverSync.SyncAsync(dbContext, drivers, cancellationToken);
    await TruckSync.SyncAsync(dbContext, trucks, cancellationToken);
    await TrailerSync.SyncAsync(dbContext, trailers, cancellationToken);
    var count = await FleetAssignmentSync.SyncAsync(
      dbContext, assignments, trailerAssignments, snapshotTime, cancellationToken);

    count += await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    if (count > 0)
    {
      cache.Remove(FleetCache.CacheKey);
      cache.Remove("fleet-driver-ids");
      cache.Remove("assignment-sync-signature");
      cache.Remove("dispatch-sync-signature");
      reads.Invalidate("board");
    }

    return RequestResponse<int>.Ok(count);
  }
}
