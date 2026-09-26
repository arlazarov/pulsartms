namespace Application.Features.Fleet.Interfaces;

public record CameraImage(
  string Status,
  string? Url = null,
  DateTimeOffset? CapturedAt = null
);

public sealed record CameraRequest(string Id, string Scope);

public interface ITruckCameraProvider
{
  Task<CameraImage> LatestAsync(string vehicleId, CancellationToken ct);
  Task<CameraRequest> RequestAsync(
    string vehicleId,
    DateTimeOffset time,
    CancellationToken ct
  );
  Task<CameraImage> GetAsync(
    string vehicleId,
    CameraRequest retrieval,
    CancellationToken ct
  );
}
