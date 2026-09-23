using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Client.Models.DTO;

namespace Client.Services;

public class ApiService(HttpClient httpClient)
{
  private static readonly JsonSerializerOptions ResponseOptions = new(
    JsonSerializerDefaults.Web
  )
  {
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
      PlanningJsonContext.Default,
      new DefaultJsonTypeInfoResolver()
    ),
  };

  // Where the API is served, to tell an administrator an address to give
  // another system.
  public Uri? BaseAddress => httpClient.BaseAddress;

  public async Task<RequestResponseDTO<T>> GetAsync<T>(
    string url,
    CancellationToken cancellationToken = default
  )
  {
    try
    {
      using var response = await httpClient.GetAsync(
        url,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken
      );

      return await ReadResponseAsync<T>(response, cancellationToken);
    }
    catch (Exception ex)
    {
      return Fail<T>(ex.Message);
    }
  }

  public async Task<RequestResponseDTO<TResponse>> PostAsync<
    TRequest,
    TResponse
  >(string url, TRequest request, CancellationToken cancellationToken = default)
  {
    try
    {
      using var message = new HttpRequestMessage(HttpMethod.Post, url)
      {
        Content = JsonContent.Create(request),
      };
      using var response = await httpClient.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken
      );

      return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }
    catch (Exception ex)
    {
      return Fail<TResponse>(ex.Message);
    }
  }

  public async Task<RequestResponseDTO<TResponse>> PutAsync<
    TRequest,
    TResponse
  >(string url, TRequest request, CancellationToken cancellationToken = default)
  {
    try
    {
      using var message = new HttpRequestMessage(HttpMethod.Put, url)
      {
        Content = JsonContent.Create(request),
      };
      using var response = await httpClient.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken
      );

      return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }
    catch (Exception ex)
    {
      return Fail<TResponse>(ex.Message);
    }
  }

  public async Task<RequestResponseDTO<TResponse>> PatchAsync<
    TRequest,
    TResponse
  >(string url, TRequest request, CancellationToken cancellationToken = default)
  {
    try
    {
      using var message = new HttpRequestMessage(HttpMethod.Patch, url)
      {
        Content = JsonContent.Create(request),
      };
      using var response = await httpClient.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken
      );

      return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }
    catch (Exception ex)
    {
      return Fail<TResponse>(ex.Message);
    }
  }

  public async Task<RequestResponseDTO<T>> DeleteAsync<T>(
    string url,
    CancellationToken cancellationToken = default
  )
  {
    try
    {
      using var message = new HttpRequestMessage(HttpMethod.Delete, url);
      using var response = await httpClient.SendAsync(
        message,
        HttpCompletionOption.ResponseHeadersRead,
        cancellationToken
      );

      return await ReadResponseAsync<T>(response, cancellationToken);
    }
    catch (Exception ex)
    {
      return Fail<T>(ex.Message);
    }
  }

  private static async Task<RequestResponseDTO<T>> ReadResponseAsync<T>(
    HttpResponseMessage response,
    CancellationToken cancellationToken
  )
  {
    var result = await ReadResponseCoreAsync<T>(response, cancellationToken);
    result.HttpStatusCode = response.StatusCode;
    return result;
  }

  private static async Task<RequestResponseDTO<T>> ReadResponseCoreAsync<T>(
    HttpResponseMessage response,
    CancellationToken cancellationToken
  )
  {
    if (response.StatusCode == HttpStatusCode.Unauthorized)
      return Fail<T>("Your session has expired. Please sign in again.");
    if (response.StatusCode == HttpStatusCode.Forbidden)
      return Fail<T>("You do not have permission to perform this action.");
    if (response.IsSuccessStatusCode)
    {
      try
      {
        using var stream = new ResponsiveReadStream(
          await response.Content.ReadAsStreamAsync(cancellationToken)
        );
        return await JsonSerializer.DeserializeAsync<RequestResponseDTO<T>>(
            stream,
            ResponseOptions,
            cancellationToken
          ) ?? Fail<T>("The server returned an empty response.");
      }
      catch (JsonException)
      {
        return Fail<T>("The server returned an invalid response.");
      }
    }
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(content))
      return Fail<T>(
        $"The server returned {(int)response.StatusCode} without a response."
      );
    try
    {
      using var json = JsonDocument.Parse(content);
      if (json.RootElement.TryGetProperty("success", out _))
      {
        var result = JsonSerializer.Deserialize<RequestResponseDTO<T>>(
          content,
          ResponseOptions
        );
        if (
          result is not null
          && (response.IsSuccessStatusCode || !result.Success)
        )
          return result;
      }
      if (json.RootElement.TryGetProperty("title", out var title))
        return Fail<T>(Readable(title.GetString()));
    }
    catch (JsonException) { }
    return Fail<T>(
      $"The request failed (HTTP {(int)response.StatusCode}). Please try again."
    );
  }

  private static RequestResponseDTO<T> Fail<T>(string error)
  {
    return new RequestResponseDTO<T> { Success = false, Errors = [error] };
  }

  // A server that crashed answers with the name of the class that threw, and
  // a dispatcher was shown "System.ArgumentException" where a sentence
  // belongs. A title is passed on only when it reads as one.
  private static string Readable(string? title) =>
    title?.Trim() is { Length: > 0 } text
    && (text.Contains(' ') || !text.Contains('.'))
      ? text
      : "The request failed. Please try again.";
}
