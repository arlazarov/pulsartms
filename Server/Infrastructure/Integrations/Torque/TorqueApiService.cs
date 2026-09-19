using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Features.Integrations.Interfaces;
using Application.Features.Integrations.Models;
using Application.Features.Synchronization.Options;
using Infrastructure.Integrations.Http;
using Infrastructure.Integrations.Torque.Models;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.Torque;

public class TorqueApiService
{
  private readonly HttpClient _httpClient;
  private readonly IIntegrationCredentials _credentials;
  private readonly SynchronizationOptions _sync;

  public TorqueApiService(
    HttpClient httpClient,
    IConfiguration configuration,
    IIntegrationCredentials credentials
  )
  {
    _httpClient = httpClient;
    _sync =
      configuration.GetSection("Synchronization").Get<SynchronizationOptions>()
      ?? new();
    _credentials = credentials;

    var baseUrl =
      configuration["TorqueAI:BaseUrl"]
      ?? throw new InvalidOperationException(
        "TorqueAI:BaseUrl is not configured."
      );

    if (
      !baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
      && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    )
      baseUrl = $"https://{baseUrl}";

    _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
  }

  public async Task<List<TorqueDispatchDto>> GetDispatchesAsync(
    CancellationToken cancellationToken = default
  )
  {
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var loads = await GetDispatchesAsync(
      today.AddDays(-_sync.DispatchLookbackDays),
      today.AddDays(_sync.DispatchLookaheadDays),
      cancellationToken
    );
    // API dates are order creation dates. Keep current/upcoming deliveries and
    // a
    // short delivery history instead of importing every old order in that
    // window.
    return loads
      .Where(x =>
        !DateOnly.TryParse(x.DeliveryDate, out var end)
        || end >= today.AddDays(-7)
      )
      .ToList();
  }

  public async Task<List<TorqueDispatchDto>> GetDispatchesAsync(
    DateOnly from,
    DateOnly to,
    CancellationToken cancellationToken = default
  )
  {
    const int limit = 500;
    var dispatches = new List<TorqueDispatchDto>();
    var budget = new ProviderReadBudget();
    var requests = 0;

    if (to < from)
      throw new ArgumentException("Dispatch date range is reversed.");
    for (
      var windowStart = from;
      windowStart <= to;
      windowStart = windowStart.AddDays(30)
    )
    {
      var windowEnd =
        windowStart.AddDays(29) < to ? windowStart.AddDays(29) : to;
      var page = 1;
      while (true)
      {
        if (++requests > 256)
          throw ProviderReadBudget.TooLarge();
        var url =
          $"api/external/dispatches?from={windowStart:yyyy-MM-dd}&to={windowEnd:yyyy-MM-dd}&page={page}&limit={limit}";

        var values = await _credentials.GetAsync(
          IntegrationProviderCatalog.Torque,
          cancellationToken
        );
        var apiKey = values.Get("apiKey");
        if (string.IsNullOrWhiteSpace(apiKey))
          throw new InvalidOperationException(
            "TorqueAI API key is not configured."
          );
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
          "Bearer",
          apiKey
        );
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
          cancellationToken
        );
        timeout.CancelAfter(_httpClient.Timeout);
        using var message = await _httpClient.SendAsync(
          request,
          HttpCompletionOption.ResponseHeadersRead,
          timeout.Token
        );
        if (!message.IsSuccessStatusCode)
          throw new HttpRequestException(
            "Upstream API request failed.",
            null,
            message.StatusCode
          );
        var response =
          await ProviderJson.ReadAsync<TorqueDispatchResponse>(
            message,
            budget,
            timeout.Token
          )
          ?? throw new InvalidOperationException(
            "TorqueAI returned an empty response."
          );

        if (
          response.TotalCount > 0
          && (response.ItemsPerPage <= 0 || response.Data.Count == 0)
        )
          throw new InvalidOperationException(
            "TorqueAI returned invalid pagination data."
          );
        if (
          response.TotalCount < 0
          || response.ItemsPerPage < 0
          || response.Data.Count > limit
        )
          throw new InvalidOperationException(
            "TorqueAI returned invalid pagination data."
          );
        if (dispatches.Count + response.Data.Count > 100000)
          throw ProviderReadBudget.TooLarge();
        dispatches.AddRange(response.Data);

        if ((long)page * response.ItemsPerPage >= response.TotalCount)
          break;

        page++;
      }
    }

    return dispatches.GroupBy(x => x.LoadNumber).Select(x => x.Last()).ToList();
  }
}
