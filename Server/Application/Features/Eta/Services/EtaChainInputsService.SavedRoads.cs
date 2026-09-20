using Application.Features.Eta.Models;
using Application.Features.Execution.Models;
using Application.Features.Routing.Exceptions;
using Application.Features.Routing.Models;

namespace Application.Features.Eta.Services;

public sealed partial class EtaChainInputsService
{
  internal async Task RequireCurrentRoadsAsync(
    EtaChainDescription expected,
    RoutePlan? plan,
    CancellationToken ct
  )
  {
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException(
        "ETA road validation requires a publication transaction."
      );
    var roots = expected.Roads.Roots;
    var legacy = await rootRoutes.ReadManyAsync(
      roots
        .Where(x => !x.Work.ExecutionLegId.HasValue)
        .Select(x => x.Work.DispatchId)
        .ToArray(),
      ct
    );
    var legIds = roots
      .Where(x => x.Work.ExecutionLegId.HasValue)
      .Select(x => x.Work.ExecutionLegId!.Value)
      .ToArray();
    var native =
      legIds.Length == 0
        ? new Dictionary<Guid, SavedRoutePlanMetadata>()
        : await rootRoutes.ReadExecutionLegsAsync(legIds, ct);
    foreach (var root in roots)
    {
      var saved = root.Work.ExecutionLegId is { } legId
        ? native.GetValueOrDefault(legId)
        : legacy.GetValueOrDefault(root.Work.DispatchId);
      if (RootVersion(root.Work, saved) != root)
        throw RoadsChanged();
    }
    // A cached current plan must match the metadata used to select this root.
    if (roots[^1].PlanSignature != PlanSignature(plan))
      throw RoadsChanged();
    var future = expected.Roads.Future;
    if (!future.IsEmpty)
    {
      // Each future load is checked where its roads live: under its leg once
      // it has been accepted, under its dispatch while it is only assigned.
      var actual = new List<NextLoadRouteVersion>();
      var plain = future
        .Where(x => !x.ExecutionLegId.HasValue)
        .Select(x => x.DispatchId)
        .ToArray();
      var legs = future
        .Where(x => x.ExecutionLegId.HasValue)
        .Select(x => x.ExecutionLegId!.Value)
        .ToArray();
      if (plain.Length > 0)
        actual.AddRange(await savedRoutes.ReadVersionsAsync(plain, ct));
      if (legs.Length > 0)
        actual.AddRange(await savedRoutes.ReadExecutionVersionsAsync(legs, ct));
      RequireFutureRoads(future, actual);
    }
  }

  private static void RequireFutureRoads(
    IEnumerable<NextLoadRouteVersion> expected,
    IEnumerable<NextLoadRouteVersion> actual
  )
  {
    if (
      !expected
        .OrderBy(x => x.DispatchId)
        .SequenceEqual(actual.OrderBy(x => x.DispatchId))
    )
      throw RoadsChanged();
  }

  private static EtaRootRoadVersion RootVersion(
    WorkIdentity work,
    SavedRoutePlanMetadata? saved
  )
  {
    var plan = Hash(
      saved is null
        ? null
        : new
        {
          saved.PlanId,
          saved.PlanTruckId,
          saved.PlanExecutionLegId,
          saved.AssignmentRevision,
          saved.Version,
          Tracking = SavedRoadVersion.TrackingSignature(saved.Tracking),
        }
    );
    return new(
      work,
      Hash(
        new
        {
          saved?.InputHash,
          saved?.TruckId,
          saved?.ExecutionLegId,
          Plan = plan,
        }
      ),
      plan
    );
  }

  private static string PlanSignature(RoutePlan? plan) =>
    Hash(
      plan is null
        ? null
        : new
        {
          PlanId = plan.Id,
          PlanTruckId = plan.TruckId,
          PlanExecutionLegId = plan.ExecutionLegId,
          plan.AssignmentRevision,
          plan.Version,
          Tracking = SavedRoadVersion.TrackingSignature(plan.Tracking),
        }
    );

  private static RoutePlanningException RoadsChanged() =>
    new("Saved roads changed. Refresh the ETA inputs.");
}
