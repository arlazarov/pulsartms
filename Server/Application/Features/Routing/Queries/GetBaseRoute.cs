using System.Text.Json;
using Application.Features.Routing.Models;
using Application.Features.Routing.Services;
using Application.Features.Routing.Services.Routes;
using Application.Models;

namespace Application.Features.Routing.Queries;

public sealed record GetBaseRouteQuery(
  Guid DispatchId,
  Guid? ExecutionLegId = null
) : IRequest<RequestResponse<TruckRoute?>>;

public sealed class GetBaseRouteHandler(IAppDbContext db)
  : IRequestHandler<GetBaseRouteQuery, RequestResponse<TruckRoute?>>
{
  public async Task<RequestResponse<TruckRoute?>> Handle(
    GetBaseRouteQuery request,
    CancellationToken ct
  )
  {
    var saved = await db
      .DispatchBaseRoutes.AsNoTracking()
      .SingleOrDefaultAsync(
        x =>
          x.DispatchId == request.DispatchId
          && x.ExecutionLegId == request.ExecutionLegId,
        ct
      );
    return RequestResponse<TruckRoute?>.Ok(
      saved is null
        ? null
        : JsonSerializer.Deserialize<TruckRoute>(
          saved.RouteJson,
          RoutePlanningService.Json
        )
    );
  }
}
