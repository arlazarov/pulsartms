using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

public interface IFleetTelemetryFeedProvider
{
  Task<TelemetryFeed> GetFeedAsync(string? cursor, CancellationToken ct);
}
