using System.Net;
using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Queries.GetTruckCamera;

public record GetTruckCameraQuery(Guid TruckId, Guid RequestId)
  : IRequest<RequestResponse<CameraImage>>;

public sealed class GetTruckCameraHandler(
  ITruckCameraProvider provider,
  IMemoryCache cache,
  IAppDbContext db,
  ICurrentCompany company
) : IRequestHandler<GetTruckCameraQuery, RequestResponse<CameraImage>>
{
  public async Task<RequestResponse<CameraImage>> Handle(
    GetTruckCameraQuery request,
    CancellationToken ct
  )
  {
    if (
      !cache.TryGetValue<CameraRetrieval>(
        $"camera-request:{request.RequestId}",
        out var retrieval
      )
      || retrieval is null
      || retrieval.CompanyId != company.Id
      || retrieval.TruckId != request.TruckId
      || !await db
        .Trucks.AsNoTracking()
        .AnyAsync(
          x =>
            x.Id == request.TruckId
            && x.IsActive
            && x.ExternalId == retrieval.VehicleId,
          ct
        )
    )
      return RequestResponse<CameraImage>.Fail(
        "Camera request expired. Request a new image.",
        404
      );
    try
    {
      var image = await provider.GetAsync(
        retrieval.VehicleId,
        retrieval.Request,
        ct
      );
      return image.Status == "expired"
        ? RequestResponse<CameraImage>.Fail(
          "Camera connection changed. Request a new image.",
          404
        )
        : RequestResponse<CameraImage>.Ok(image);
    }
    catch (HttpRequestException ex)
      when (ex.StatusCode
          is HttpStatusCode.Forbidden
            or HttpStatusCode.Unauthorized
      )
    {
      return RequestResponse<CameraImage>.Fail(
        "Samsara requires Read Media Retrieval permission.",
        403
      );
    }
  }
}
