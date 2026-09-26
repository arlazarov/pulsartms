using Application.Caching;
using Application.Concurrency;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Commands.SyncFleet;

public record SyncFleetCommand : IRequest<RequestResponse<int>>;

public class SyncFleetHandler(
  IAppDbContext dbContext,
  IFleetProvider fleetProvider,
  IMemoryCache cache,
  ReadCache reads,
  ICurrentCompany companies
) : IRequestHandler<SyncFleetCommand, RequestResponse<int>>
{
  public async Task<RequestResponse<int>> Handle(
    SyncFleetCommand request,
    CancellationToken cancellationToken
  )
  {
    await ProcessGates.Fleet.WaitAsync(cancellationToken);
    try
    {
      return await SyncAsync(request, cancellationToken);
    }
    finally
    {
      ProcessGates.Fleet.Release();
    }
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
      snapshotTime,
      snapshotTime,
      cancellationToken
    );
    var trailerAssignments = await fleetProvider.GetTrailerAssignmentsAsync(
      drivers.Select(x => x.ExternalId).ToArray(),
      cancellationToken
    );

    await using var transaction =
      await dbContext.Database.BeginTransactionAsync(cancellationToken);

    await DriverSync.SyncAsync(dbContext, drivers, cancellationToken);
    await TruckSync.SyncAsync(dbContext, trucks, cancellationToken);
    await TrailerCatalog.ApplyAsync(
      dbContext,
      fleetProvider.Source,
      trailers,
      cancellationToken
    );
    var count = await FleetAssignmentSync.SyncAsync(
      dbContext,
      assignments,
      trailerAssignments,
      snapshotTime,
      fleetProvider.Source,
      cancellationToken
    );

    count += await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    if (count > 0)
    {
      FleetSyncKeys.Forget(cache, companies.Id);
      reads.Invalidate(ReadGroups.FleetCatalog);
      reads.Invalidate(ReadGroups.Board);
    }

    return RequestResponse<int>.Ok(count);
  }
}
