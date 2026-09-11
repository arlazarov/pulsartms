using Application.Features.Fleet.Models;

namespace Application.Features.Fleet.Interfaces;

public interface IFleetTelemetryFeedProvider
{
  Task<TelemetryFeed> GetFeedAsync(string? cursor, CancellationToken ct);
}
