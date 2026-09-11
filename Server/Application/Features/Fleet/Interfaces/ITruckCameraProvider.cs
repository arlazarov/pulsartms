namespace Application.Features.Fleet.Interfaces;

public record CameraImage(string Status, string? Url = null, DateTimeOffset? CapturedAt = null);

public interface ITruckCameraProvider
{
  Task<CameraImage> LatestAsync(string vehicleId, CancellationToken ct);
  Task<string> RequestAsync(string vehicleId, DateTimeOffset time, CancellationToken ct);
  Task<CameraImage> GetAsync(string vehicleId, string retrievalId, CancellationToken ct);
}
