using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Models;

namespace Application.Features.Dispatch.Queries;

public sealed record DispatchTruckStatus(
  Guid TruckId,
  decimal Speed,
  string EngineState,
  string TrailerNumber
);

public sealed record GetDispatchTelemetryQuery(Guid[] TruckIds)
  : IRequest<RequestResponse<List<DispatchTruckStatus>>>,
    IChecked
{
  public IEnumerable<string> Wrong()
  {
    if (TruckIds is not { Length: <= 12 })
      yield return "Ask for between one and twelve trucks.";
    else if (TruckIds.Any(id => id == Guid.Empty))
      yield return "One of the trucks asked for was never chosen.";
  }
}

public sealed class GetDispatchTelemetryHandler(ISender mediator)
  : IRequestHandler<
    GetDispatchTelemetryQuery,
    RequestResponse<List<DispatchTruckStatus>>
  >
{
  public async Task<RequestResponse<List<DispatchTruckStatus>>> Handle(
    GetDispatchTelemetryQuery request,
    CancellationToken ct
  )
  {
    if (request.TruckIds.Length == 0)
      return RequestResponse<List<DispatchTruckStatus>>.Ok([]);
    var fleet = await mediator.Send(new GetFleetLocationsQuery(), ct);
    if (!fleet.Success)
      return RequestResponse<List<DispatchTruckStatus>>.Fail(
        "Truck status is temporarily unavailable."
      );
    var ids = request.TruckIds.ToHashSet();
    return RequestResponse<List<DispatchTruckStatus>>.Ok(
      fleet
        .Response?.Trucks.Where(truck => ids.Contains(truck.TruckId))
        .Select(truck => new DispatchTruckStatus(
          truck.TruckId,
          truck.Speed,
          truck.EngineState,
          truck.TrailerNumber
        ))
        .ToList() ?? []
    );
  }
}
