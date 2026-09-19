using System.Net.Http.Json;

namespace Infrastructure.Integrations.Http;

public abstract class BaseApiService(HttpClient httpClient)
{
  protected readonly HttpClient HttpClient = httpClient;

  protected virtual Task PrepareRequestAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken
  ) => Task.CompletedTask;

  public async Task<T?> GetAsync<T>(
    string url,
    CancellationToken cancellationToken = default
  )
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    return await SendAsync<T>(request, cancellationToken);
  }

  private protected async Task<T?> GetPageAsync<T>(
    string url,
    ProviderReadBudget budget,
    CancellationToken ct
  )
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    return await SendCoreAsync<T>(request, ct, budget);
  }

  protected async Task<TResponse?> PostAsync<TRequest, TResponse>(
    string url,
    TRequest request,
    CancellationToken cancellationToken = default
  )
  {
    using var message = new HttpRequestMessage(HttpMethod.Post, url)
    {
      Content = JsonContent.Create(request),
    };
    return await SendAsync<TResponse>(message, cancellationToken);
  }

  protected Task<TResponse?> SendAsync<TResponse>(
    HttpRequestMessage request,
    CancellationToken cancellationToken = default
  ) => SendCoreAsync<TResponse>(request, cancellationToken, new());

  private async Task<TResponse?> SendCoreAsync<TResponse>(
    HttpRequestMessage request,
    CancellationToken cancellationToken = default,
    ProviderReadBudget? budget = null
  )
  {
    await PrepareRequestAsync(request, cancellationToken);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken
    );
    timeout.CancelAfter(HttpClient.Timeout);
    using var response = await HttpClient.SendAsync(
      request,
      HttpCompletionOption.ResponseHeadersRead,
      timeout.Token
    );
    if (!response.IsSuccessStatusCode)
    {
      // Provider URLs and bodies can contain credentials or personal data.
      throw new HttpRequestException(
        "Upstream API request failed.",
        null,
        response.StatusCode
      );
    }

    return await ProviderJson.ReadAsync<TResponse>(
      response,
      budget ?? new(),
      timeout.Token
    );
  }
}
