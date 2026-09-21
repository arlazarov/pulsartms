using System.Security.Cryptography;
using System.Text.Json;
using Application.Caching;
using Application.Features.Routing.Background;
using Domain.Entities.Execution;

namespace Domain.Models.Execution;

internal static class ExecutionCommandSupport
{
  public static async Task<Guid?> ActorAsync(
    IAppDbContext db,
    ICurrentUser caller,
    IUserRoleService roles,
    CancellationToken ct
  )
  {
    if (!caller.IsAuthenticated || string.IsNullOrEmpty(caller.IdentityUserId))
      return null;
    if (
      await roles.GetAsync(caller.IdentityUserId, ct)
      is not ("Admin" or "Dispatch")
    )
      return null;
    return await db
      .Users.AsNoTracking()
      .Where(x => x.IdentityUserId == caller.IdentityUserId && x.IsActive)
      .Select(x => (Guid?)x.Id)
      .SingleOrDefaultAsync(ct);
  }

  public static string Hash<T>(T request) =>
    Convert.ToHexString(
      SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request))
    );

  public static SwitchResult Result(DispatchSwitchOperation operation) =>
    new(
      operation.Id,
      operation.Status,
      operation.Revision,
      operation
        .Participants.OrderBy(x => x.DispatchId)
        .Select(x => new SwitchLegResult(
          x.DispatchId,
          x.OutgoingLegId,
          x.IncomingLegId
        )
        {
          ParticipantId = x.Id,
          ReleaseVisitId = x.ReleaseVisitId,
          ReceiveVisitId = x.ReceiveVisitId,
          TransferKind = x.TransferKind,
          Revision = x.Revision,
          ReleasedAt = x.ReleasedAt,
          ReceivedAt = x.ReceivedAt,
        })
        .ToArray()
    );

  public static void Invalidate(
    DispatchSwitchOperation operation,
    IEnumerable<ExecutionLeg> legs,
    ReadCache reads,
    RoutePreparationQueue preparation
  )
  {
    foreach (var participant in operation.Participants)
    {
      var id = participant.DispatchId;
      reads.Invalidate($"route:{id}");
      reads.Invalidate($"route:{id}:leg:{participant.OutgoingLegId}");
      reads.Invalidate($"route:{id}:leg:{participant.IncomingLegId}");
      preparation.MarkDirty(id);
    }
    foreach (var id in legs.Select(x => x.TruckId).Distinct())
      preparation.MarkTruckDirty(id);
    foreach (
      var key in new[] { "dispatch", "board", "execution", "route-previews" }
    )
      reads.Invalidate(key);
  }

  public static ExecutionAssignment Assignment(ExecutionLeg leg)
  {
    var stops = ExecutionStopRows.Read(leg);
    var current =
      stops.FirstOrDefault(x => !x.IsCompleted) ?? stops.LastOrDefault();
    return new(
      leg.TruckId,
      current is null ? leg.DriverId : current.DriverId,
      leg.TrailerId,
      current is null ? leg.CoDriverId : current.CoDriverId
    );
  }
}
