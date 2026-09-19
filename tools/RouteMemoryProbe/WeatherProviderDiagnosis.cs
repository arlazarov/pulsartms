using System.Text.Json;
using Infrastructure.Integrations.Google.Weather;
using Microsoft.Extensions.Configuration;

internal static class WeatherProviderDiagnosis
{
  public static async Task RunAsync()
  {
    var config = new ConfigurationBuilder()
      .AddUserSecrets("pulsartms-api-local")
      .Build();
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    var result = await new GoogleWeatherProvider(
      client,
      config
    ).GetCurrentAsync(35.5m, -99m, default);
    Console.WriteLine(
      JsonSerializer.Serialize(
        new
        {
          available = result is not null,
          result?.Celsius,
          result?.Condition,
          result?.UpdatedAt,
        }
      )
    );
  }
}
