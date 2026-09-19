using Application.Features.Dispatch.Models;
using Domain.Entities.Execution;

namespace Application.Features.Dispatch.Services;

public static class DispatchWorkspaceTransfers
{
  public static DispatchWorkspaceTransfer? Project(
    SwitchParticipant participant,
    Guid visitId,
    IReadOnlyCollection<ExecutionLeg> legs,
    IReadOnlyDictionary<Guid, string> trucks,
    IReadOnlyDictionary<Guid, string> trailers,
    IReadOnlyDictionary<Guid, string> drivers
  )
  {
    var release = participant.ReleaseVisitId == visitId;
    if (
      participant.IsCancelled
      || !release && participant.ReceiveVisitId != visitId
    )
      return null;
    var outgoing = legs.SingleOrDefault(x => x.Id == participant.OutgoingLegId);
    var incoming = legs.SingleOrDefault(x => x.Id == participant.IncomingLegId);
    if (outgoing is null || incoming is null)
      return null;
    var confirmed = release
      ? participant.ReleasedBy.HasValue
      : participant.ReceivedBy.HasValue;
    var trailerTransfer = participant.TransferKind == "drop_hook";
    return new()
    {
      SwitchId = participant.SwitchId,
      Kind = participant.TransferKind,
      Action = trailerTransfer
        ? release
          ? "drop"
          : "hook"
        : release
          ? "release"
          : "receive",
      Status = confirmed ? "confirmed" : "planned",
      ActualAt = confirmed
        ? release
          ? participant.ReleasedAt
          : participant.ReceivedAt
        : null,
      OutgoingTruckNumber = trucks.GetValueOrDefault(outgoing.TruckId, ""),
      IncomingTruckNumber = trucks.GetValueOrDefault(incoming.TruckId, ""),
      OutgoingTrailerNumber = trailers.GetValueOrDefault(
        outgoing.TrailerId ?? Guid.Empty,
        ""
      ),
      IncomingTrailerNumber = trailers.GetValueOrDefault(
        incoming.TrailerId ?? Guid.Empty,
        ""
      ),
      OutgoingDriverName = drivers.GetValueOrDefault(
        outgoing.DriverId ?? Guid.Empty,
        ""
      ),
      IncomingDriverName = drivers.GetValueOrDefault(
        incoming.DriverId ?? Guid.Empty,
        ""
      ),
    };
  }
}
