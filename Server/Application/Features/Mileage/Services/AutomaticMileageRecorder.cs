using System.Data;
using System.Security.Cryptography;
using System.Text;
using Application.Features.Execution.Models;
using Application.Features.Mileage.Interfaces;
using Application.Features.Routing.Algorithms;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services.Routes;
using Domain.Entities.Mileage;

namespace Application.Features.Mileage.Services;

public sealed partial class AutomaticMileageRecorder(
  IAppDbContext db,
  TruckPlanningProfileService profiles,
  TimeProvider clock
) : IAutomaticMileageRecorder
{
  public async Task<bool> CapturePlannedAsync(
    Guid executionLegId,
    CancellationToken ct
  )
  {
    try
    {
      return await CaptureSavedRouteAsync(executionLegId, ct);
    }
    catch (Exception exception) when (db.IsWriteConflict(exception))
    {
      return false;
    }
  }

  private async Task<bool> CaptureSavedRouteAsync(
    Guid executionLegId,
    CancellationToken ct
  )
  {
    await using var transaction = await db.Database.BeginTransactionAsync(
      IsolationLevel.Serializable,
      ct
    );
    var leg = await db
      .ExecutionLegs.AsNoTracking()
      .Include(x => x.Loads)
      .SingleOrDefaultAsync(x => x.Id == executionLegId, ct);
    if (leg is null || leg.Status == "cancelled")
      return true;
    if (leg.SourceReviewReason is not null)
      return false;
    if (!await db.LockExecutionLegAsync(leg.Id, leg.Revision, ct))
      return false;
    var stops = ExecutionStopRows.Read(leg).ToArray();
    if (stops.Length is < 2 or > 49)
      return stops.Length < 2;
    if (stops.Select(x => x.Id).Distinct().Count() != stops.Length)
      return false;
    var saved = await db
      .DispatchBaseRoutes.AsNoTracking()
      .SingleOrDefaultAsync(x => x.ExecutionLegId == leg.Id, ct);
    if (saved is null)
      return false;
    var load = await db
      .Dispatches.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == saved.DispatchId, ct);
    if (load is null || !leg.Loads.Any(x => x.DispatchId == load.Id))
      return false;
    var profile = await profiles.GetAsync(leg.TruckId, ct);
    var hash = BaseRouteService.Signature(
      RouteWorkProjection.Capture(load, leg, stops),
      profile
    );
    var route =
      saved.InputHash == hash
        ? SavedRouteReader.Route(saved.RouteJson, stops.Length - 1)
        : null;
    var points = stops
      .Select(x => new RoutePoint(
        (double)(x.Latitude ?? decimal.MinValue),
        (double)(x.Longitude ?? decimal.MinValue)
      ))
      .ToArray();
    if (
      route is null
      || !RouteAnchoring.Matches(route, points)
      || route.Legs.Any(x =>
        !double.IsFinite(x.Miles) || x.Miles is < 0 or > 1_000_000
      )
    )
      return false;
    var policy = await PolicyAsync(ct);
    var existing = await db
      .Movements.Where(x =>
        x.ExecutionLegId == leg.Id && x.Origin == "native-route"
      )
      .ToListAsync(ct);
    var keys = new HashSet<Guid>();
    var now = clock.GetUtcNow().UtcDateTime;
    var reference = $"{saved.Id}:{saved.InputHash}";
    for (var index = 0; index < route.Legs.Count; index++)
    {
      var scope = MileageSegmentScope.Create(leg, stops, index);
      var identity = scope.Identity("native-route");
      var key = Key(identity);
      keys.Add(key);
      var miles = decimal.Round((decimal)route.Legs[index].Miles, 3);
      var movement = existing.SingleOrDefault(x => x.IdempotencyKey == key);
      if (movement is null)
      {
        movement = scope.Movement("native-route", key, Hash(identity), now);
        db.Movements.Add(movement);
        db.MovementAllocationEvents.Add(
          MileageMutation.Allocate(
            movement,
            MileageAllocation.Resolve(movement, policy),
            policy.Revision,
            false,
            Guid.Empty,
            now
          )
        );
      }
      else if (
        !movement.PlannedSuperseded
        && movement.PlannedMiles == miles
        && movement.PlannedSourceReference == reference
      )
        continue;
      else
        movement.Revision++;
      movement.PlannedSuperseded = false;
      var evidence = new MovementDistanceEvidence
      {
        Id = Guid.NewGuid(),
        MovementId = movement.Id,
        Revision = movement.Revision,
        Basis = "planned",
        Miles = miles,
        Source = "saved-native-road",
        SourceReference = reference,
        Reason = "validated-stop-to-stop-route",
        ObservedAt = saved.CalculatedAt,
        RecordedAt = now,
        RecordedBy = Guid.Empty,
      };
      db.MovementDistanceEvidence.Add(evidence);
      movement.PlannedMiles = miles;
      movement.PlannedAt = evidence.ObservedAt;
      movement.PlannedSource = evidence.Source;
      movement.PlannedSourceReference = evidence.SourceReference;
      movement.PlannedEvidenceId = evidence.Id;
    }
    foreach (
      var movement in existing.Where(x =>
        !x.PlannedSuperseded && !keys.Contains(x.IdempotencyKey)
      )
    )
    {
      movement.PlannedSuperseded = true;
      movement.Revision++;
      db.MovementAllocationEvents.Add(
        MileageMutation.Allocate(
          movement,
          new(
            movement.AllocatedDispatchId,
            movement.AllocationTarget,
            "planned-segment-superseded"
          ),
          movement.PolicyRevision,
          movement.ManualOverride,
          Guid.Empty,
          now
        )
      );
    }
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return true;
  }

  private async Task<MileageAllocationPolicy> PolicyAsync(
    CancellationToken ct
  ) =>
    await db
      .MileageAllocationPolicies.AsNoTracking()
      .SingleOrDefaultAsync(
        x => x.Id == MileageAllocationPolicy.SingletonId,
        ct
      ) ?? new MileageAllocationPolicy();

  private static string Hash(string text) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

  private static Guid Key(string text) =>
    new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
}
