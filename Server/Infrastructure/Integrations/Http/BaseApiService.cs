using System.Net.Http.Json;

namespace Infrastructure.Integrations.Http;

public abstract class BaseApiService(HttpClient httpClient)
{
  protected readonly HttpClient HttpClient = httpClient;

  protected virtual Task PrepareRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.CompletedTask;

  public async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken = default)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    return await SendAsync<T>(request, cancellationToken);
  }

  protected async Task<TResponse?> PostAsync<TRequest, TResponse>(
    string url,
    TRequest request,
    CancellationToken cancellationToken = default
  )
  {
    using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(request) };
    return await SendAsync<TResponse>(message, cancellationToken);
  }

  protected async Task<TResponse?> SendAsync<TResponse>(
    HttpRequestMessage request,
    CancellationToken cancellationToken = default
  )
  {
    await PrepareRequestAsync(request, cancellationToken);
    using var response = await HttpClient.SendAsync(request, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
      // Provider URLs and bodies can contain credentials or personal data.
      throw new HttpRequestException("Upstream API request failed.", null, response.StatusCode);
    }

    return await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken);
  }
}
