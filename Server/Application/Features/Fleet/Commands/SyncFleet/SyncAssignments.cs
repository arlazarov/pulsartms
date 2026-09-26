using System.Security.Cryptography;
using System.Text.Json;
using Application.Caching;
using Application.Concurrency;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Fleet.Services;
using Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Commands.SyncFleet;

public record SyncAssignmentsCommand : IRequest<RequestResponse<int>>;

public sealed class SyncAssignmentsHandler(
  IAppDbContext db,
  IFleetProvider provider,
  IMemoryCache cache,
  ReadCache reads,
  ICurrentCompany companies
) : IRequestHandler<SyncAssignmentsCommand, RequestResponse<int>>
{
  public async Task<RequestResponse<int>> Handle(
    SyncAssignmentsCommand request,
    CancellationToken ct
  )
  {
    await ProcessGates.Fleet.WaitAsync(ct);
    try
    {
      return await SyncAsync(request, ct);
    }
    finally
    {
      ProcessGates.Fleet.Release();
    }
  }

  private async Task<RequestResponse<int>> SyncAsync(
    SyncAssignmentsCommand request,
    CancellationToken ct
  )
  {
    if (
      !cache.TryGetValue<string[]>(
        FleetSyncKeys.DriverIds(companies.Id),
        out var drivers
      )
    )
    {
      drivers = await db
        .Drivers.AsNoTracking()
        .Where(x => x.IsActive)
        .Select(x => x.ExternalId)
        .ToArrayAsync(ct);
      cache.Set(
        FleetSyncKeys.DriverIds(companies.Id),
        drivers,
        TimeSpan.FromHours(1)
      );
    }
    var now = DateTime.UtcNow;
    var assignments = await provider.GetAssignmentsAsync(now, now, ct);
    var trailers = await provider.GetTrailerAssignmentsAsync(drivers!, ct);
    var signature = Convert.ToHexString(
      SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(
          new
          {
            Assignments = assignments
              .Where(x =>
                x.StartTime <= now && (x.EndTime is null || x.EndTime > now)
              )
              .OrderBy(x => x.VehicleExternalId)
              .ThenBy(x => x.DriverExternalId)
              .ThenBy(x => x.StartTime),
            Trailers = trailers
              .Where(x =>
                x.StartTime <= now && (x.EndTime is null || x.EndTime > now)
              )
              .OrderBy(x => x.DriverExternalId)
              .ThenBy(x => x.TrailerExternalId)
              .ThenBy(x => x.StartTime),
          }
        )
      )
    );
    if (
      cache.Get<string>(FleetSyncKeys.AssignmentSignature(companies.Id))
      == signature
    )
    {
      // The provider said nothing new, but the current work may have moved
      // on - a load picked up, a leg accepted - so the trucks' trailers are
      // resolved again from what is stored.
      await using var unchanged = await db.Database.BeginTransactionAsync(ct);
      var moved = await TruckTrailerAssignments.ResolveAsync(db, ct);
      if (moved.Count == 0)
        return RequestResponse<int>.Ok(0);
      await db.SaveChangesAsync(ct);
      await unchanged.CommitAsync(ct);
      TruckTrailerAssignments.Published(reads);
      return RequestResponse<int>.Ok(moved.Count);
    }
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    var count = await FleetAssignmentSync.SyncAsync(
      db,
      assignments,
      trailers,
      now,
      provider.Source,
      ct
    );
    count += await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    cache.Set(
      FleetSyncKeys.AssignmentSignature(companies.Id),
      signature,
      TimeSpan.FromMinutes(30)
    );
    if (count > 0)
    {
      reads.Invalidate(ReadGroups.FleetCatalog);
      reads.Invalidate(ReadGroups.Board);
    }
    return RequestResponse<int>.Ok(count);
  }
}
