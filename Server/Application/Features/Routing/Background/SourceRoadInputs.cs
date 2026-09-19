using System.Security.Cryptography;
using System.Text.Json;
using Application.Features.Execution.Interfaces;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Deadheads;
using Application.Features.Routing.Services.Routes;

namespace Application.Features.Routing.Background;

public sealed record SourceRoadObservation(Guid? TruckId, string Signature);

public sealed class SourceRoadInputs(
  IAppDbContext db,
  TruckPlanningProfileService profiles,
  DeadheadHistoryService history,
  IExecutionReadScope readScope
)
{
  public async Task<SourceRoadObservation?> ReadAsync(
    Guid dispatchId,
    CancellationToken ct
  ) => (await ReadAsync([dispatchId], ct)).GetValueOrDefault(dispatchId);

  public Task<IReadOnlyDictionary<Guid, SourceRoadObservation>> ReadAsync(
    IReadOnlyCollection<Guid> dispatchIds,
    CancellationToken ct
  ) =>
    readScope.ReadAsync<IReadOnlyDictionary<Guid, SourceRoadObservation>>(
      async token =>
      {
        var sources = await db
          .Dispatches.AsNoTracking()
          .Include(x => x.Stops)
          .Where(x => dispatchIds.Contains(x.Id))
          .ToListAsync(token);
        var native = await ExecutionRouteSections.ReadAsync(db, sources, token);
        var work = sources
          .SelectMany(x =>
            native.GetValueOrDefault(x.Id)
            ?? [RouteWorkProjection.Capture(x.TruckItinerary())]
          )
          .ToArray();
        var predecessors = await history.ReadSectionsAsync(work, token);
        var effectiveProfiles = new Dictionary<Guid, TruckRouteProfile>();
        var numbers = sources
          .Select(x => x.TruckNumber)
          .Where(x => !string.IsNullOrWhiteSpace(x))
          .Distinct()
          .ToArray();
        var trucks = await db
          .Trucks.AsNoTracking()
          .Where(x => numbers.Contains(x.UnitNumber))
          .ToDictionaryAsync(x => x.UnitNumber, x => x.Id, token);
        var result = new Dictionary<Guid, SourceRoadObservation>();
        foreach (var source in sources)
        {
          var sections = native.GetValueOrDefault(source.Id);
          var loads =
            sections ?? [RouteWorkProjection.Capture(source.TruckItinerary())];
          var routing = new List<string>();
          foreach (var load in loads)
          {
            var truck =
              load.TruckId ?? trucks.GetValueOrDefault(load.TruckNumber ?? "");
            if (!effectiveProfiles.TryGetValue(truck, out var profile))
            {
              profile = await profiles.GetUncachedAsync(truck, token);
              effectiveProfiles.Add(truck, profile);
            }
            routing.Add(BaseRouteService.Signature(load, profile));
          }
          result.Add(
            source.Id,
            new(
              RoutePreparationInputs.Truck(source),
              Convert.ToHexString(
                SHA256.HashData(
                  JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                      Source = RouteWorkProjection.Capture(source),
                      Sections = sections,
                      Routing = routing,
                      Predecessors = loads.Select(x =>
                        predecessors
                          .GetValueOrDefault((x.Id, x.ExecutionLegId))
                          ?.InputSignature
                      ),
                    }
                  )
                )
              )
            )
          );
        }
        return result;
      },
      ct
    );
}
