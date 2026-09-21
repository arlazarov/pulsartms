using System.Net;
using Application.Features.Fleet.Interfaces;
using Application.Models;
using Domain.Models.Fleet;
using Microsoft.Extensions.Caching.Memory;

namespace Application.Features.Fleet.Commands.RequestTruckCamera;

public record RequestTruckCameraCommand(Guid TruckId)
  : IRequest<RequestResponse<Guid>>;

public sealed class RequestTruckCameraHandler(
  IAppDbContext db,
  ITruckCameraProvider provider,
  IMemoryCache cache
) : IRequestHandler<RequestTruckCameraCommand, RequestResponse<Guid>>
{
  public async Task<RequestResponse<Guid>> Handle(
    RequestTruckCameraCommand request,
    CancellationToken ct
  )
  {
    var vehicle = await db
      .Trucks.AsNoTracking()
      .Where(x => x.Id == request.TruckId && x.IsActive)
      .Select(x => x.ExternalId)
      .SingleOrDefaultAsync(ct);
    if (string.IsNullOrWhiteSpace(vehicle))
      return RequestResponse<Guid>.Fail(
        "Camera is not available for this truck.",
        404
      );
    try
    {
      var providerId = await provider.RequestAsync(
        vehicle,
        DateTimeOffset.UtcNow.AddSeconds(-5),
        ct
      );
      var id = Guid.NewGuid();
      cache.Set(
        $"camera-request:{id}",
        new CameraRetrieval(request.TruckId, vehicle, providerId),
        TimeSpan.FromMinutes(10)
      );
      return RequestResponse<Guid>.Ok(id);
    }
    catch (HttpRequestException ex)
      when (ex.StatusCode
          is HttpStatusCode.Forbidden
            or HttpStatusCode.Unauthorized
      )
    {
      return RequestResponse<Guid>.Fail(
        "Samsara requires Read and Write Media Retrieval permissions.",
        403
      );
    }
    catch (HttpRequestException ex)
      when (ex.StatusCode == HttpStatusCode.TooManyRequests)
    {
      return RequestResponse<Guid>.Fail(
        "Samsara camera quota or request limit reached. Try later.",
        429
      );
    }
  }
}
