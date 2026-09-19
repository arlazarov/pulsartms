using System.Net.Http.Headers;
using System.Text.Json;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Infrastructure.Integrations.Http;
using Infrastructure.Integrations.Samsara.Models;

namespace Infrastructure.Integrations.Samsara;

public class SamsaraApiService : BaseApiService
{
  private readonly IIntegrationCredentials _credentials;

  public Task<JsonElement> RequestCameraImageAsync(
    string vehicleId,
    DateTimeOffset time,
    CancellationToken ct
  ) =>
    PostAsync<object, JsonElement>(
      "cameras/media/retrieval",
      new
      {
        vehicleId,
        startTime = time.ToString("O"),
        endTime = time.ToString("O"),
        mediaType = "image",
        inputs = new[] { "dashcamRoadFacing" },
      },
      ct
    );

  public SamsaraApiService(
    HttpClient httpClient,
    IIntegrationCredentials credentials
  )
    : base(httpClient)
  {
    _credentials = credentials;
    HttpClient.BaseAddress = new Uri("https://api.samsara.com/");
  }

  protected override async Task PrepareRequestAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken
  )
  {
    var destination = new Uri(HttpClient.BaseAddress!, request.RequestUri!);
    if (
      destination.Scheme != Uri.UriSchemeHttps
      || destination.Host != "api.samsara.com"
      || destination.Port != 443
      || destination.UserInfo.Length != 0
    )
      throw new InvalidOperationException(
        "Samsara request destination is not allowed."
      );
    request.RequestUri = destination;
    var values = await _credentials.GetAsync(
      IntegrationProviderCatalog.Samsara,
      cancellationToken
    );
    var token = values.Get("apiKey");

    if (string.IsNullOrWhiteSpace(token))
    {
      throw new InvalidOperationException(
        "Samsara API token is not configured."
      );
    }

    request.Headers.Authorization = new AuthenticationHeaderValue(
      "Bearer",
      token
    );
  }

  public Task<IReadOnlyList<SamsaraHosHistory>> GetHosHistoryAsync(
    string driverId,
    DateTimeOffset from,
    DateTimeOffset to,
    CancellationToken ct
  ) =>
    GetDataAsync<SamsaraHosHistory>(
      "fleet/hos/logs?driverIds="
        + Uri.EscapeDataString(driverId)
        + "&startTime="
        + Uri.EscapeDataString(from.ToString("O"))
        + "&endTime="
        + Uri.EscapeDataString(to.ToString("O")),
      ct
    );

  public Task<IReadOnlyList<SamsaraHosClock>> GetHosClocksAsync(
    CancellationToken ct
  ) => GetDataAsync<SamsaraHosClock>("fleet/hos/clocks", ct);

  public Task<IReadOnlyList<SamsaraDriver>> GetDriversAsync(
    CancellationToken cancellationToken = default
  ) => GetDataAsync<SamsaraDriver>("fleet/drivers", cancellationToken);

  public Task<IReadOnlyList<SamsaraVehicle>> GetVehiclesAsync(
    CancellationToken cancellationToken = default
  ) => GetDataAsync<SamsaraVehicle>("fleet/vehicles", cancellationToken);

  public Task<IReadOnlyList<SamsaraTrailer>> GetTrailersAsync(
    CancellationToken cancellationToken = default
  ) => GetDataAsync<SamsaraTrailer>("fleet/trailers", cancellationToken);

  public Task<IReadOnlyList<SamsaraVehicleAssignment>> GetAssignmentsAsync(
    DateTime startTime,
    DateTime endTime,
    CancellationToken cancellationToken = default
  ) =>
    GetDataAsync<SamsaraVehicleAssignment>(
      "fleet/driver-vehicle-assignments?filterBy=vehicles&assignmentType=HOS"
        + $"&startTime={Uri.EscapeDataString(startTime.ToUniversalTime().ToString("O"))}"
        + $"&endTime={Uri.EscapeDataString(endTime.ToUniversalTime().ToString("O"))}",
      cancellationToken
    );

  public async Task<
    IReadOnlyList<SamsaraTrailerAssignment>
  > GetTrailerAssignmentsAsync(
    IReadOnlyCollection<string> driverIds,
    CancellationToken cancellationToken = default
  )
  {
    var assignments = new List<SamsaraTrailerAssignment>();
    var firstBatch = true;
    foreach (
      var batch in driverIds
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct()
        .Chunk(50)
    )
    {
      if (!firstBatch)
        await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
      firstBatch = false;
      var ids = string.Join(",", batch.Select(Uri.EscapeDataString));
      assignments.AddRange(
        await GetDataAsync<SamsaraTrailerAssignment>(
          $"driver-trailer-assignments?driverIds={ids}",
          cancellationToken
        )
      );
    }
    return assignments;
  }

  public Task<IReadOnlyList<SamsaraVehicleLocation>> GetVehicleLocationsAsync(
    CancellationToken cancellationToken = default
  ) =>
    GetDataAsync<SamsaraVehicleLocation>(
      "/fleet/vehicles/stats?types=gps,engineStates,fuelPercents",
      cancellationToken
    );

  public async Task<SamsaraResponse<SamsaraStatsFeed>> GetStatsFeedAsync(
    string? cursor,
    CancellationToken ct
  )
  {
    var url = "fleet/vehicles/stats/feed?types=gps,engineStates,fuelPercents";
    if (!string.IsNullOrEmpty(cursor))
      url += "&after=" + Uri.EscapeDataString(cursor);
    return await GetAsync<SamsaraResponse<SamsaraStatsFeed>>(url, ct)
      ?? throw new InvalidOperationException(
        "Samsara returned an empty telemetry feed."
      );
  }

  public async Task<SamsaraResponse<SamsaraOdometerFeed>> GetOdometerFeedAsync(
    string? cursor,
    CancellationToken ct
  )
  {
    var url = "fleet/vehicles/stats/feed?types=obdOdometerMeters";
    if (!string.IsNullOrEmpty(cursor))
      url += "&after=" + Uri.EscapeDataString(cursor);
    return await GetAsync<SamsaraResponse<SamsaraOdometerFeed>>(url, ct)
      ?? throw new InvalidOperationException(
        "Samsara returned an empty odometer feed."
      );
  }

  public Task<
    IReadOnlyList<SamsaraVehicleLocation>
  > GetOutsideTemperaturesAsync(CancellationToken ct) =>
    GetDataAsync<SamsaraVehicleLocation>(
      "fleet/vehicles/stats?types=ambientAirTemperatureMilliC",
      ct
    );

  public async Task<SamsaraLocationSpeedStream> GetLocationSpeedStreamAsync(
    IReadOnlyCollection<string> assetIds,
    DateTime startTime,
    DateTime endTime,
    string? after = null,
    CancellationToken cancellationToken = default
  )
  {
    if (assetIds.Count == 0)
    {
      return new SamsaraLocationSpeedStream();
    }

    var ids = string.Join(",", assetIds.Select(Uri.EscapeDataString));

    var url =
      "/assets/location-and-speed/stream"
      + $"?ids={ids}"
      + $"&startTime={Uri.EscapeDataString(startTime.ToString("O"))}"
      + $"&endTime={Uri.EscapeDataString(endTime.ToString("O"))}"
      + "&includeSpeed=true"
      + "&includeReverseGeo=true"
      + "&includeHighFrequencyLocations=true";

    if (!string.IsNullOrWhiteSpace(after))
    {
      url += $"&after={Uri.EscapeDataString(after)}";
    }

    return await GetAsync<SamsaraLocationSpeedStream>(url, cancellationToken)
      ?? new SamsaraLocationSpeedStream();
  }

  private async Task<IReadOnlyList<T>> GetDataAsync<T>(
    string endpoint,
    CancellationToken cancellationToken
  )
  {
    var items = new List<T>();
    var budget = new ProviderReadBudget();
    var pages = 0;
    var cursors = new HashSet<string>(StringComparer.Ordinal);
    var url = endpoint;
    while (true)
    {
      if (++pages > 256)
        throw ProviderReadBudget.TooLarge();
      var response =
        await GetPageAsync<SamsaraResponse<T>>(url, budget, cancellationToken)
        ?? throw new InvalidOperationException(
          "Samsara returned an empty response."
        );
      if (items.Count + response.Data.Count > 100000)
        throw ProviderReadBudget.TooLarge();
      items.AddRange(response.Data);
      if (response.Pagination?.HasNextPage != true)
        return items;

      var cursor = response.Pagination.EndCursor;
      if (string.IsNullOrWhiteSpace(cursor) || !cursors.Add(cursor))
        throw new InvalidOperationException(
          "Samsara returned an invalid pagination cursor."
        );
      url =
        endpoint
        + (endpoint.Contains('?') ? "&" : "?")
        + "after="
        + Uri.EscapeDataString(cursor);
      await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
    }
  }
}

public class SamsaraResponse<T>
{
  public required List<T> Data { get; set; }
  public SamsaraPagination? Pagination { get; set; }
}

public class SamsaraPagination
{
  public string EndCursor { get; set; } = string.Empty;
  public bool HasNextPage { get; set; }
}
