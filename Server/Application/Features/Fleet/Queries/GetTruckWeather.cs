using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Features.Routing.Algorithms;
using Application.Models;

namespace Application.Features.Fleet.Queries;

public sealed record GetTruckWeatherQuery(Guid TruckId)
  : IRequest<RequestResponse<WeatherReading?>>;

public sealed class GetTruckWeatherHandler(
  ISender sender,
  IWeatherProvider weather,
  IReadCache cache,
  TimeProvider clock
) : IRequestHandler<GetTruckWeatherQuery, RequestResponse<WeatherReading?>>
{
  public async Task<RequestResponse<WeatherReading?>> Handle(
    GetTruckWeatherQuery request,
    CancellationToken ct
  )
  {
    var fleet = await sender.Send(new GetFleetLocationsQuery(true), ct);
    var truck = fleet.Response?.Trucks.FirstOrDefault(x =>
      x.TruckId == request.TruckId
    );
    if (
      TruckLocationFreshness.IsStale(truck, clock.GetUtcNow().UtcDateTime)
      || truck!.Latitude is < -90 or > 90
      || truck.Longitude is < -180 or > 180
    )
      return RequestResponse<WeatherReading?>.Ok(null);

    // Moving GPS must not cause a new paid request for each viewer.
    var key = request.TruckId.ToString("N");
    var reading = await cache
      .GetAsync<WeatherReading?>(
        "fleet-weather",
        key,
        async () =>
        {
          using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(8)
          );
          return await weather.GetCurrentAsync(
            truck.Latitude,
            truck.Longitude,
            timeout.Token
          );
        },
        TimeSpan.FromMinutes(10)
      )
      .WaitAsync(ct);
    if (
      reading is not null
      && (
        reading.UpdatedAt < clock.GetUtcNow().AddHours(-1)
        || reading.UpdatedAt > clock.GetUtcNow().AddMinutes(5)
      )
    )
      reading = null;
    return RequestResponse<WeatherReading?>.Ok(reading);
  }
}
