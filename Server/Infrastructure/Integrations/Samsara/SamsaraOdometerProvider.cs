using Application.Features.Mileage.Interfaces;
using Application.Features.Mileage.Models;

namespace Infrastructure.Integrations.Samsara;

public sealed class SamsaraOdometerProvider(SamsaraApiService samsara)
  : IOdometerFeedProvider
{
  public async Task<OdometerPage> ReadAsync(
    string? cursor,
    CancellationToken ct
  )
  {
    var feed = await samsara.GetOdometerFeedAsync(cursor, ct);
    var pagination = feed.Pagination;
    if (string.IsNullOrWhiteSpace(pagination?.EndCursor))
      throw new InvalidOperationException("Odometer feed cursor is missing.");
    var samples = feed
      .Data.SelectMany(vehicle =>
        vehicle
          .ObdOdometerMeters.Where(reading =>
            reading.Value.HasValue && reading.Time != default
          )
          .Select(reading => new OdometerSample(
            vehicle.Id,
            reading.Time,
            reading.Value!.Value
          ))
      )
      .ToArray();
    return new(samples, pagination.EndCursor, pagination.HasNextPage);
  }
}
