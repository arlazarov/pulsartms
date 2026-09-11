using Application.Features.Routing.Services.Routes;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Models;
using System.Text.Json;

namespace Application.Features.Routing.Queries;

public sealed record GetBaseRouteQuery(Guid DispatchId) : IRequest<RequestResponse<TruckRoute?>>;

public sealed class GetBaseRouteHandler(IAppDbContext db) : IRequestHandler<GetBaseRouteQuery, RequestResponse<TruckRoute?>>
{
  public async Task<RequestResponse<TruckRoute?>> Handle(GetBaseRouteQuery request, CancellationToken ct)
  {
    var saved = await db.DispatchBaseRoutes.AsNoTracking().SingleOrDefaultAsync(x => x.DispatchId == request.DispatchId, ct);
    return RequestResponse<TruckRoute?>.Ok(saved is null ? null : JsonSerializer.Deserialize<TruckRoute>(saved.RouteJson, RoutePlanningService.Json));
  }
}
