using Domain.Models.Fleet;

namespace Application.Features.Fleet.Interfaces;

public interface IWeatherProvider
{
  Task<WeatherReading?> GetCurrentAsync(
    decimal latitude,
    decimal longitude,
    CancellationToken ct
  );
}
