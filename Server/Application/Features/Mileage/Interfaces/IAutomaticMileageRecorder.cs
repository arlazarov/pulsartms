using Application.Features.Mileage.Models;

namespace Application.Features.Mileage.Interfaces;

public interface IAutomaticMileageRecorder
{
  Task<bool> CapturePlannedAsync(Guid executionLegId, CancellationToken ct);
  Task<string?> ReadOdometerCursorAsync(CancellationToken ct);
  Task<bool> CaptureOdometerAsync(
    string? expectedCursor,
    OdometerPage page,
    CancellationToken ct
  );
}

public interface IOdometerFeedProvider
{
  Task<OdometerPage> ReadAsync(string? cursor, CancellationToken ct);
}
