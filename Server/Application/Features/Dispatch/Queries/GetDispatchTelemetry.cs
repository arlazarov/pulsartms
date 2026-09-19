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
  : IRequest<RequestResponse<List<DispatchTruckStatus>>>;

public sealed class GetDispatchTelemetryValidator
  : AbstractValidator<GetDispatchTelemetryQuery>
{
  public GetDispatchTelemetryValidator()
  {
    RuleFor(x => x.TruckIds).NotNull().Must(ids => ids is { Length: <= 12 });
    RuleForEach(x => x.TruckIds).NotEmpty();
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
