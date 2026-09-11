using Application.Features.Fleet.Interfaces;
using Application.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Net;


namespace Application.Features.Fleet.Queries.GetLatestTruckCamera;

public record GetLatestTruckCameraQuery(Guid TruckId) : IRequest<RequestResponse<CameraImage>>;

public sealed class GetLatestTruckCameraHandler(IAppDbContext db, ITruckCameraProvider provider)
  : IRequestHandler<GetLatestTruckCameraQuery, RequestResponse<CameraImage>>
{
  public async Task<RequestResponse<CameraImage>> Handle(GetLatestTruckCameraQuery request, CancellationToken ct)
  {
    var vehicle = await db.Trucks.AsNoTracking().Where(x => x.Id == request.TruckId && x.IsActive)
      .Select(x => x.ExternalId).SingleOrDefaultAsync(ct);
    if (string.IsNullOrWhiteSpace(vehicle)) return RequestResponse<CameraImage>.Fail("Camera is not available for this truck.", 404);
    try
    {
      return RequestResponse<CameraImage>.Ok(await provider.LatestAsync(vehicle, ct));
    }
    catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
    { return RequestResponse<CameraImage>.Fail("Samsara requires Read Media Retrieval permission.", 403); }
  }

}
