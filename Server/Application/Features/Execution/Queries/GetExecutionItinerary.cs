using Application.Features.Execution.Models;
using Application.Features.Execution.Services;
using Application.Features.Routing.Exceptions;
using Domain.Entities.Dispatch;
using Domain.Entities.Execution;

namespace Application.Features.Execution.Queries;

public sealed record ExecutionItinerary(
  ExecutionLeg Leg,
  IReadOnlyList<DispatchStop> Stops
)
{
  public bool AwaitingReceipt { get; init; }
  public Guid? BlockingParticipantId { get; init; }
  public DateTime? ReleasedAt { get; init; }
  public DateTime? ReceivedAt { get; init; }
}

public sealed record GetExecutionItineraryQuery(
  Guid DispatchId,
  Guid? ExecutionLegId = null,
  Guid? TruckId = null
) : IRequest<ExecutionItinerary?>;

public sealed class GetExecutionItineraryHandler(IAppDbContext db)
  : IRequestHandler<GetExecutionItineraryQuery, ExecutionItinerary?>
{
  public async Task<ExecutionItinerary?> Handle(
    GetExecutionItineraryQuery request,
    CancellationToken ct
  )
  {
    var legs = await db
      .LoadExecutionLegs.AsNoTracking()
      .Where(x => x.DispatchId == request.DispatchId)
      .OrderBy(x => x.Sequence)
      .Select(x => x.ExecutionLeg)
      .ToListAsync(ct);
    if (legs.Count == 0)
    {
      if (request.ExecutionLegId.HasValue)
        throw new RoutePlanningException("Execution leg not found.");
      return null;
    }
    var candidates = legs.Where(x => x.Status is "active" or "planned")
      .Where(x =>
        !request.ExecutionLegId.HasValue || x.Id == request.ExecutionLegId
      )
      .Where(x => !request.TruckId.HasValue || x.TruckId == request.TruckId)
      .ToList();
    var active = candidates.Where(x => x.Status == "active").ToList();
    var leg =
      request.ExecutionLegId.HasValue ? candidates.SingleOrDefault()
      : active.Count == 1 ? active[0]
      : request.TruckId.HasValue && active.Count == 0 && candidates.Count == 1
        ? candidates[0]
      : null;
    if (leg is null)
      throw new RoutePlanningException(
        "Select the confirmed execution leg for this load."
      );
    var stops = ExecutionStopRows.Read(leg);
    if (
      stops.Count is < 1 or > 49
      || stops.Select(x => x.Id).Distinct().Count() != stops.Count
    )
      throw new RoutePlanningException("Execution leg visits need review.");
    var participants = await db
      .SwitchParticipants.AsNoTracking()
      .Where(x =>
        !x.IsCancelled
        && (x.OutgoingLegId == leg.Id || x.IncomingLegId == leg.Id)
      )
      .ToListAsync(ct);
    var actuals = ExecutionTransfers.Project([leg], participants);
    if (
      leg.StartSwitchId.HasValue
        && !participants.Any(x =>
          x.SwitchId == leg.StartSwitchId
          && x.IncomingLegId == leg.Id
          && x.ReceiveVisitId == stops[0].Id
        )
      || leg.EndSwitchId.HasValue
        && !participants.Any(x =>
          x.SwitchId == leg.EndSwitchId
          && x.OutgoingLegId == leg.Id
          && x.ReleaseVisitId == stops[^1].Id
        )
    )
      throw new RoutePlanningException(
        "Execution transfer boundaries need review."
      );
    foreach (var stop in stops)
    {
      if (!actuals.TryGetValue(stop.Id, out var visit))
        continue;
      ExecutionSnapshots.ApplyActual(stop, visit);
    }
    if (leg.EndSwitchId.HasValue)
    {
      var release = participants.Single(x => x.OutgoingLegId == leg.Id);
      stops[^1].AwaitingHandoff = !release.ReleasedBy.HasValue;
    }
    var receiving = leg.StartSwitchId.HasValue
      ? participants.Single(x => x.IncomingLegId == leg.Id)
      : null;
    var awaiting = receiving is not null && !receiving.ReceivedBy.HasValue;
    if (awaiting)
      stops[0].AwaitingHandoff = true;
    return new(leg, stops)
    {
      AwaitingReceipt = awaiting,
      BlockingParticipantId = awaiting ? receiving!.Id : null,
      ReleasedAt = receiving?.ReleasedAt,
      ReceivedAt = receiving?.ReceivedAt,
    };
  }
}
