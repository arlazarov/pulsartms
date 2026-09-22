using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Application.Caching;
using Application.Concurrency;
using Application.Diagnostics;
using Application.Features.Dispatch.Interfaces;
using Application.Features.Dispatch.Models;
using Application.Features.Dispatch.Options;
using Application.Features.Dispatch.Services;
using Application.Features.Execution.Services;
using Application.Features.Routing.Background;
using Application.Models;
using Domain.Entities.Dispatch;
using Domain.Rules;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using DispatchEntity = global::Domain.Entities.Dispatch.Dispatch;

namespace Application.Features.Dispatch.Commands.SyncDispatche;

public record SyncDispatchesCommand : IRequest<RequestResponse<int>>;

public partial class SyncDispatchesCommandHandler(
  IAppDbContext dbContext,
  IEnumerable<IDispatchProvider> providers,
  IOptions<DispatchImportOptions> importOptions,
  ReadCache reads,
  IMemoryCache memory,
  RoutePreparationQueue preparation
) : IRequestHandler<SyncDispatchesCommand, RequestResponse<int>>
{
  private sealed record LoadSnapshot(string Signature, DateTime ReconciledAt);

  private sealed record Snapshot(
    long CatalogGeneration,
    long DispatchGeneration,
    IReadOnlyDictionary<string, LoadSnapshot> Loads
  );

  public async Task<RequestResponse<int>> Handle(
    SyncDispatchesCommand request,
    CancellationToken cancellationToken
  )
  {
    await ProcessGates.Dispatch.WaitAsync(cancellationToken);
    try
    {
      return await SyncAsync(request, cancellationToken);
    }
    catch (Exception ex)
      when (dbContext.IsWriteConflict(ex) || ex is RoutePlanningException)
    {
      return RequestResponse<int>.Fail(
        "Dispatch execution changed concurrently. Synchronization will retry.",
        409
      );
    }
    finally
    {
      ProcessGates.Dispatch.Release();
    }
  }

  private async Task<RequestResponse<int>> SyncAsync(
    SyncDispatchesCommand request,
    CancellationToken cancellationToken
  )
  {
    var providerKey = (importOptions.Value.Provider ?? "").Trim();
    if (providerKey.Length == 0)
      return RequestResponse<int>.Fail("Load import is disabled.", 409);
    var dispatchProvider = providers.SingleOrDefault(x =>
      x.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase)
    );
    if (dispatchProvider is null)
      return RequestResponse<int>.Fail("Load import is unavailable.", 503);
    providerKey = dispatchProvider.Key;
    var memoryKey = $"dispatch-sync-signature:{providerKey}";
    var catalogGeneration = reads.Generation("fleet-catalog");
    var dispatchGeneration = reads.Generation("dispatch");
    IReadOnlyList<ExternalDispatch> allSources;
    using (PerformanceStages.Start("dispatch-sync", "provider-wait"))
      allSources = await dispatchProvider.GetDispatchesAsync(cancellationToken);
    if (
      allSources.Any(x =>
        string.IsNullOrWhiteSpace(x.ExternalId)
        || x.ExternalId.Length > 200
        || x.ExternalId.Any(char.IsControl)
      )
      || allSources.Select(x => x.ExternalId).Distinct().Count()
        != allSources.Count
    )
      return RequestResponse<int>.Fail(
        "The import contains missing or duplicate source identities.",
        422
      );
    using var processing = PerformanceStages.Start(
      "dispatch-sync",
      "reconciliation"
    );
    var fingerprints = allSources.ToDictionary(
      x => x.ExternalId,
      x =>
        Convert.ToHexString(
          SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(x))
        )
    );
    var previous = memory.Get<Snapshot>(memoryKey);
    var reusable =
      previous?.CatalogGeneration == catalogGeneration
      && previous.DispatchGeneration == dispatchGeneration;
    var repairAfter = DateTime.UtcNow.AddMinutes(-30);
    var sources = allSources
      .Where(x =>
        !reusable
        || previous!.Loads.GetValueOrDefault(x.ExternalId) is not { } saved
        || saved.Signature != fingerprints[x.ExternalId]
        || saved.ReconciledAt <= repairAfter
      )
      .ToArray();
    PerformanceStages.Count("dispatch-sync", "source-loads", allSources.Count);
    PerformanceStages.Count("dispatch-sync", "changed-loads", sources.Length);
    if (sources.Length == 0)
      return RequestResponse<int>.Ok(0);
    await using var transaction =
      await dbContext.Database.BeginTransactionAsync(
        IsolationLevel.Serializable,
        cancellationToken
      );
    var drivers = await dbContext.Drivers.ToListAsync(cancellationToken);
    var driverIndex = new DriverMatcher.Index(drivers);
    var truckNumbers = sources
      .Select(x => x.TruckNumber)
      .Concat(sources.SelectMany(x => x.Stops).Select(x => x.TruckNumber))
      .Distinct()
      .ToArray();
    var trailerNumbers = sources
      .Select(x => x.TrailerNumber)
      .Concat(sources.SelectMany(x => x.Stops).Select(x => x.TrailerNumber))
      .Distinct()
      .ToArray();
    var customerNames = sources
      .Select(x => CustomerMatcher.Normalize(x.CustomerName))
      .Distinct()
      .ToArray();
    var trucks = (
      await dbContext
        .Trucks.Where(x => truckNumbers.Contains(x.UnitNumber))
        .ToListAsync(cancellationToken)
    )
      .GroupBy(x => x.UnitNumber)
      .Where(x => x.Count() == 1)
      .ToDictionary(x => x.Key, x => x.Single(), StringComparer.Ordinal);
    var trailers = (
      await dbContext
        .Trailers.Where(x => trailerNumbers.Contains(x.UnitNumber))
        .ToListAsync(cancellationToken)
    )
      .GroupBy(x => x.UnitNumber)
      .Where(x => x.Count() == 1)
      .ToDictionary(x => x.Key, x => x.Single(), StringComparer.Ordinal);
    var customers = (
      await dbContext
        .Customers.Where(x => customerNames.Contains(x.NormalizedName))
        .ToListAsync(cancellationToken)
    )
      .GroupBy(x => x.NormalizedName)
      .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
    var externalIds = sources.Select(x => x.ExternalId).ToArray();
    var links = await dbContext
      .DispatchSourceLinks.Include(x => x.Dispatch)
      .ThenInclude(x => x.Stops)
      .Where(x =>
        x.Provider == providerKey && externalIds.Contains(x.ExternalId)
      )
      .ToListAsync(cancellationToken);
    var dispatches = links.ToDictionary(x => x.ExternalId, x => x.Dispatch);
    var newSources = sources
      .Where(x => !dispatches.ContainsKey(x.ExternalId))
      .OrderBy(x => x.ExternalId, StringComparer.Ordinal)
      .ToArray();
    var numbers = await DispatchNumbers.ReserveAsync(
      dbContext,
      newSources.Select(x => (int?)x.LoadNumber).ToArray(),
      cancellationToken
    );
    var reserved = newSources
      .Select((x, index) => (x.ExternalId, Number: numbers[index]))
      .ToDictionary(x => x.ExternalId, x => x.Number);

    var syncedAt = DateTime.UtcNow;
    var dispatchIds = dispatches.Values.Select(x => x.Id).ToArray();
    var workspaces = await dbContext
      .DispatchWorkspaces.Where(x => dispatchIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, cancellationToken);
    var dirty = new HashSet<Guid>();
    var affectedTrucks = new HashSet<Guid>();

    var scope = new SyncScope(
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
    );
    foreach (var source in sources)
      Apply(source, scope);

    var executions = await ExecutionSourceReconciliation.ApplyAsync(
      dbContext,
      dispatches.Values.ToArray(),
      syncedAt,
      cancellationToken
    );
    var changed = await dbContext.SaveChangesAsync(cancellationToken);
    var initial = await ExecutionImportAcceptance.ApplyAsync(
      dbContext,
      links,
      syncedAt,
      cancellationToken
    );
    executions = executions.Concat(initial).ToArray();
    changed += await dbContext.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    if (changed > 0)
    {
      reads.Invalidate("dispatch");
      reads.Invalidate("board");
      if (executions.Count > 0)
      {
        reads.Invalidate("execution");
        reads.Invalidate("route-previews");
        foreach (var leg in executions)
        {
          affectedTrucks.Add(leg.TruckId);
          foreach (var link in leg.Loads)
          {
            dirty.Add(link.DispatchId);
            reads.Invalidate($"route:{link.DispatchId}:leg:{leg.Id}");
          }
        }
      }
      foreach (var id in dirty)
        preparation.MarkDirty(id);
      foreach (var id in affectedTrucks)
        preparation.MarkTruckDirty(id, reads);
      if (affectedTrucks.Count > 0)
      {
        var successors = await dbContext
          .Dispatches.AsNoTracking()
          .Where(x =>
            (x.Status == "assigned" || x.Status == "in_transit")
            && (
              x.PlanningTruckId.HasValue
                && affectedTrucks.Contains(x.PlanningTruckId.Value)
              || x.TruckId.HasValue && affectedTrucks.Contains(x.TruckId.Value)
              || x.Stops.Any(s =>
                s.TruckId.HasValue && affectedTrucks.Contains(s.TruckId.Value)
              )
            )
          )
          .OrderBy(x => x.Status == "in_transit" ? 0 : 1)
          .ThenBy(x => x.ShipDate)
          .ThenBy(x => x.Id)
          .Take(preparation.Capacity)
          .Select(x => x.Id)
          .ToListAsync(cancellationToken);
        foreach (var id in successors)
          preparation.MarkDirty(id);
      }
    }

    var reconciled = sources.Select(x => x.ExternalId).ToHashSet();
    memory.Set(
      memoryKey,
      new Snapshot(
        catalogGeneration,
        reads.Generation("dispatch"),
        fingerprints
          .Take(8192)
          .ToDictionary(
            x => x.Key,
            x =>
              reconciled.Contains(x.Key)
                ? new LoadSnapshot(x.Value, syncedAt)
                : previous!.Loads[x.Key]
          )
      ),
      TimeSpan.FromMinutes(30)
    );

    return RequestResponse<int>.Ok(changed);
  }
}
