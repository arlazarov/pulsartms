namespace Application.Features.Mileage.Interfaces;

public interface IOdometerCaptureLease
{
  Task<bool> AcquireAsync(string owner, DateTime now, CancellationToken ct);
}
