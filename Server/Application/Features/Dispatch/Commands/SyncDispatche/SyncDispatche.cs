using Application.Caching;
using Application.Features.Synchronization.Services;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using System.Security.Cryptography;
using Application.Features.Dispatch.Interfaces;
using Application.Models;
using Application.Features.Routing.Background;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public record SyncDispatchesCommand : IRequest<RequestResponse<int>>;

public class SyncDispatchesCommandHandler(
  IAppDbContext dbContext,
  IDispatchProvider dispatchProvider,
  ReadCache reads, IMemoryCache memory, RoutePreparationQueue preparation, SynchronizationGates gates
) : IRequestHandler<SyncDispatchesCommand, RequestResponse<int>>
{
  public async Task<RequestResponse<int>> Handle(
    SyncDispatchesCommand request,
    CancellationToken cancellationToken
  )
  {
    await gates.Dispatch.WaitAsync(cancellationToken);
    try { return await SyncAsync(request, cancellationToken); }
    finally { gates.Dispatch.Release(); }
  }

  private async Task<RequestResponse<int>> SyncAsync(
    SyncDispatchesCommand request,
    CancellationToken cancellationToken
  )
  {
    var sources = await dispatchProvider.GetDispatchesAsync(cancellationToken);
    var signature = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(sources.OrderBy(x => x.LoadNumber))));
    if (memory.Get<string>("dispatch-sync-signature") == signature) return RequestResponse<int>.Ok(0);
    var drivers = await dbContext.Drivers.ToListAsync(cancellationToken);
    var trucks = await dbContext.Trucks.ToListAsync(cancellationToken);
    var trailers = await dbContext.Trailers.ToListAsync(cancellationToken);
    var customers = await dbContext.Customers.ToListAsync(cancellationToken);
    var loadNumbers = sources.Select(x => x.LoadNumber).ToList();

    var dispatches = await dbContext
      .Dispatches.Include(x => x.Stops)
      .Where(x => loadNumbers.Contains(x.LoadNumber))
      .ToListAsync(cancellationToken);

    var syncedAt = DateTime.UtcNow;
    var dirty = new HashSet<Guid>();
    var affectedTrucks = new HashSet<Guid>();

    foreach (var source in sources)
    {
      var customer = CustomerMatcher.Match(customers, source.CustomerName);

      if (customer is null && !string.IsNullOrWhiteSpace(source.CustomerName))
      {
        customer = CustomerMatcher.Create(source.CustomerName);
        customers.Add(customer);
        dbContext.Customers.Add(customer);
      }

      var driver = DriverMatcher.Match(drivers, source.DriverName);
      var truck = trucks.FirstOrDefault(x => x.UnitNumber == source.TruckNumber);
      var trailer = trailers.FirstOrDefault(x => x.UnitNumber == source.TrailerNumber);
      var dispatch = dispatches.FirstOrDefault(x => x.LoadNumber == source.LoadNumber);

      if (dispatch is null)
      {
        dispatch = new Domain.Entities.Dispatch.Dispatch
        {
          Id = Guid.NewGuid(),
          LoadNumber = source.LoadNumber,
        };

        dbContext.Dispatches.Add(dispatch);
        dispatches.Add(dispatch);
      }

      var previousTrucks = dispatch.Stops.Select(x => x.TruckId).Prepend(dispatch.TruckId)
        .Where(x => x.HasValue).Select(x => x!.Value).ToArray();
      var previousInputs = RoutePreparationInputs.Signature(dispatch, RoutePreparationInputs.Truck(dispatch), 0, reads, syncedAt);
      DispatchMapper.Update(dispatch, source, customer, driver, truck, trailer, syncedAt);

      var existingStops = dispatch.Stops.ToList();
      foreach (var removed in existingStops.Where(x => !source.Stops.Any(y => y.Sequence == x.Sequence)))
      {
        dbContext.DispatchStops.Remove(removed);
        dispatch.Stops.Remove(removed);
      }
      var stopsChanged = DispatchComparer.StopsChanged(existingStops, source.Stops);
      foreach (var sourceStop in source.Stops.OrderBy(x => x.Sequence))
      {
        var stop = existingStops.FirstOrDefault(x => x.Sequence == sourceStop.Sequence);
        var stopDriver = DriverMatcher.Match(drivers, sourceStop.DriverName);
        var coDriver = DriverMatcher.Match(drivers, sourceStop.CoDriverName);
        var stopTruck = trucks.FirstOrDefault(x => x.UnitNumber == sourceStop.TruckNumber);
        var stopTrailer = trailers.FirstOrDefault(x => x.UnitNumber == sourceStop.TrailerNumber);
        if (stop is null) dispatch.Stops.Add(DispatchMapper.CreateStop(sourceStop, stopDriver, coDriver, stopTruck, stopTrailer));
        else DispatchMapper.UpdateStop(stop, sourceStop, stopDriver, coDriver, stopTruck, stopTrailer);
      }
      if (dbContext.Entry(dispatch).State is EntityState.Added or EntityState.Modified || stopsChanged)
        dispatch.LastSyncedAt = syncedAt;
      if (previousInputs != RoutePreparationInputs.Signature(dispatch, RoutePreparationInputs.Truck(dispatch), 0, reads, syncedAt))
      {
        dirty.Add(dispatch.Id);
        affectedTrucks.UnionWith(previousTrucks);
        affectedTrucks.UnionWith(dispatch.Stops.Select(x => x.TruckId).Prepend(dispatch.TruckId)
          .Where(x => x.HasValue).Select(x => x!.Value));
      }
    }

    var changed = await dbContext.SaveChangesAsync(cancellationToken);
    memory.Set("dispatch-sync-signature", signature, TimeSpan.FromMinutes(30));
    if (changed > 0)
    {
      reads.Invalidate("dispatch");
      reads.Invalidate("board");
      foreach (var id in dirty) preparation.MarkDirty(id);
      foreach (var id in affectedTrucks) preparation.MarkTruckDirty(id);
      if (affectedTrucks.Count > 0)
      {
        var successors = await dbContext.Dispatches.AsNoTracking()
          .Where(x => (x.Status == "assigned" || x.Status == "in_transit")
            && (x.TruckId.HasValue && affectedTrucks.Contains(x.TruckId.Value)
              || x.Stops.Any(s => s.TruckId.HasValue && affectedTrucks.Contains(s.TruckId.Value))))
          .OrderBy(x => x.Status == "in_transit" ? 0 : 1).ThenBy(x => x.ShipDate).ThenBy(x => x.Id)
          .Take(preparation.Capacity).Select(x => x.Id).ToListAsync(cancellationToken);
        foreach (var id in successors) preparation.MarkDirty(id);
      }
    }

    return RequestResponse<int>.Ok(changed);
  }
}
