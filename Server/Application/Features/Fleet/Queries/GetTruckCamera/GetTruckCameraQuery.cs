using System.Net;
using Application.Features.Fleet.Interfaces;
using Application.Models;
using Domain.Models.Fleet;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Queries.GetTruckCamera;

public record GetTruckCameraQuery(Guid TruckId, Guid RequestId)
  : IRequest<RequestResponse<CameraImage>>;

public sealed class GetTruckCameraHandler(
  ITruckCameraProvider provider,
  IMemoryCache cache
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
      || retrieval.TruckId != request.TruckId
    )
      return RequestResponse<CameraImage>.Fail(
        "Camera request expired. Request a new image.",
        404
      );
    try
    {
      var image = await provider.GetAsync(
        retrieval.VehicleId,
        retrieval.ProviderId,
        ct
      );
      return RequestResponse<CameraImage>.Ok(image);
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
