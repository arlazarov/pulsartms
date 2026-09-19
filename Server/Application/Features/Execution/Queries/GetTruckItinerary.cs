using Application.Features.Execution.Models;
using Application.Features.Execution.Services;

namespace Application.Features.Execution.Queries;

public sealed record GetTruckItineraryQuery(Guid TruckId, DateTimeOffset AsOf)
  : IRequest<TruckItinerarySnapshot?>;

public sealed class GetTruckItineraryHandler(TruckItineraryReader reader)
  : IRequestHandler<GetTruckItineraryQuery, TruckItinerarySnapshot?>
{
  public Task<TruckItinerarySnapshot?> Handle(
    GetTruckItineraryQuery request,
    CancellationToken ct
  ) => reader.ReadAsync(request.TruckId, request.AsOf, ct);
}
