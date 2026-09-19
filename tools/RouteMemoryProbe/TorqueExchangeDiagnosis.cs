using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

internal static class TorqueExchangeDiagnosis
{
  public static async Task RunAsync()
  {
    var config = new ConfigurationBuilder()
      .AddUserSecrets("pulsartms-api-local")
      .Build();
    var key = config["TorqueAI:ApiKey"];
    var source = config["TorqueAI:BaseUrl"];
    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(source))
      throw new InvalidOperationException(
        "Local Torque diagnostic credentials are unavailable."
      );
    if (!source.Contains("://", StringComparison.Ordinal))
      source = "https://" + source;
    using var client = new HttpClient
    {
      BaseAddress = new Uri(source.TrimEnd('/') + "/"),
      Timeout = TimeSpan.FromSeconds(30),
      MaxResponseContentBufferSize = 8 * 1024 * 1024,
    };
    var found = new HashSet<int>();
    for (var page = 1; page <= 4; page++)
    {
      var path =
        "api/external/dispatches?from=2026-09-01&to=2026-09-30"
        + $"&page={page}&limit=500";
      using var request = new HttpRequestMessage(HttpMethod.Get, path);
      request.Headers.Authorization = new AuthenticationHeaderValue(
        "Bearer",
        key
      );
      using var response = await client.SendAsync(request);
      if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException(
          $"Torque diagnostic read returned HTTP {(int)response.StatusCode}."
        );
      using var document = JsonDocument.Parse(
        await response.Content.ReadAsStreamAsync()
      );
      var root = document.RootElement;
      var loads = root.GetProperty("data");
      foreach (var load in loads.EnumerateArray())
      {
        if (
          !load.TryGetProperty("loadNumber", out var number)
          || !number.TryGetInt32(out var id)
          || id is not (1377 or 1383)
        )
          continue;
        found.Add(id);
        foreach (var stop in load.GetProperty("stops").EnumerateArray())
        {
          var operations = stop.EnumerateObject()
            .Where(x =>
              x.Name
                is "job"
                  or "operation"
                  or "action"
                  or "stopType"
                  or "operationType"
                  or "type"
            )
            .Where(x => x.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(x => x.Name, x => x.Value.GetString());
          Console.WriteLine(
            JsonSerializer.Serialize(
              new
              {
                loadNumber = id,
                sequence = Value(stop, "sequence"),
                operations,
                truckNumber = Value(stop, "truckNumber"),
                trailerNumber = Value(stop, "trailerNumber"),
                scheduled = Fields(
                  stop,
                  "scheduled",
                  [
                    "pickupDate",
                    "pickupTime",
                    "pickupDate2",
                    "pickupTime2",
                    "isWindow",
                  ]
                ),
                actual = Fields(
                  stop,
                  "actual",
                  ["arrived", "pickedUp", "delivered", "departed"]
                ),
                fieldNames = stop.EnumerateObject()
                  .Select(x => x.Name)
                  .ToArray(),
              }
            )
          );
        }
      }
      if (found.Count == 2 || loads.GetArrayLength() < 500)
        break;
    }
    Console.WriteLine(JsonSerializer.Serialize(new { inspectedLoads = found }));
  }

  private static JsonElement? Value(JsonElement source, string name) =>
    source.TryGetProperty(name, out var value) ? value.Clone() : null;

  private static Dictionary<string, JsonElement>? Fields(
    JsonElement source,
    string name,
    string[] allowed
  ) =>
    source.TryGetProperty(name, out var value)
    && value.ValueKind == JsonValueKind.Object
      ? value
        .EnumerateObject()
        .Where(x => allowed.Contains(x.Name))
        .ToDictionary(x => x.Name, x => x.Value.Clone())
      : null;
}
