using Application.Features.Dispatch.Models;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public record GetTruckDispatchQuery(Guid TruckId)
  : IRequest<RequestResponse<List<DispatchResponse>>>;

public class GetTruckDispatchQueryHandler(IAppDbContext dbContext)
  : IRequestHandler<GetTruckDispatchQuery, RequestResponse<List<DispatchResponse>>>
{
  public async Task<RequestResponse<List<DispatchResponse>>> Handle(
    GetTruckDispatchQuery request,
    CancellationToken cancellationToken
  )
  {
    var dispatches = await dbContext
      .Dispatches.AsNoTracking()
      .Where(x =>
        (x.TruckId == request.TruckId || x.Stops.Any(s => s.TruckId == request.TruckId)) && (x.Status == "assigned" || x.Status == "in_transit")
      )
      .Select(DispatchProjection.Details)
      .ToListAsync(cancellationToken);

    dispatches =
    [
      .. dispatches.OrderBy(x => x.Status == "in_transit" ? 0 : 1).ThenBy(GetNextStopDateTime),
    ];

    return RequestResponse<List<DispatchResponse>>.Ok(dispatches);
  }

  private static DateTime GetNextStopDateTime(DispatchResponse dispatch)
  {
    var stop = dispatch.Stops.FirstOrDefault(x =>
      x.Job == "Pick Up" ? x.PickedUpAt is null
      : x.Job == "Drop Off" ? x.DeliveredAt is null
      : x.DepartedAt is null
    );

    if (stop?.ScheduledDate is null)
      return DateTime.MaxValue;

    return stop.ScheduledDate.Value.ToDateTime(stop.ScheduledTime ?? TimeOnly.MinValue);
  }
}
