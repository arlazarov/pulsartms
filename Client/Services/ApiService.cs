using System.Net.Http.Json;
using Client.Models.DTO;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Client.Services;

public class ApiService(HttpClient httpClient)
{
  private static readonly System.Text.Json.JsonSerializerOptions ResponseOptions = new(System.Text.Json.JsonSerializerDefaults.Web)
  {
    TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
      PlanningJsonContext.Default, new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver())
  };
  public async Task<RequestResponseDTO<T>> GetAsync<T>(
    string url,
    CancellationToken cancellationToken = default
  )
  {
    try
    {
      using var response = await httpClient.GetAsync(url, cancellationToken);

      return await ReadResponseAsync<T>(response, cancellationToken);
    }
    catch (Exception ex)
    {
      return Fail<T>(ex.Message);
    }
  }

  // Conditional reads bypass the browser cache so a 304 reaches the caller instead of being
  // replaced by a cached body it would deserialize again.
  public async Task<RequestResponseDTO<T>> GetAsync<T>(
    string url,
    string? ifNoneMatch,
    CancellationToken cancellationToken = default
  )
  {
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, url);
      request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
      if (!string.IsNullOrEmpty(ifNoneMatch)) request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
      using var response = await httpClient.SendAsync(request, cancellationToken);
      if (response.StatusCode == System.Net.HttpStatusCode.NotModified && !string.IsNullOrEmpty(ifNoneMatch))
        return new() { Success = true, NotModified = true, ETag = ifNoneMatch, HttpStatusCode = response.StatusCode };
      var result = await ReadResponseAsync<T>(response, cancellationToken);
      result.ETag = response.Headers.ETag?.ToString();
      return result;
    }
    catch (Exception ex)
    {
      return Fail<T>(ex.Message);
    }
  }

  public async Task<RequestResponseDTO<TResponse>> PostAsync<TRequest, TResponse>(
    string url,
    TRequest request,
    CancellationToken cancellationToken = default
  )
  {
    try
    {
      using var response = await httpClient.PostAsJsonAsync(url, request, cancellationToken);

      return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }
    catch (Exception ex)
    {
      return Fail<TResponse>(ex.Message);
    }
  }

  public async Task<RequestResponseDTO<TResponse>> PutAsync<TRequest, TResponse>(
    string url,
    TRequest request,
    CancellationToken cancellationToken = default
  )
  {
    try
    {
      using var response = await httpClient.PutAsJsonAsync(url, request, cancellationToken);

      return await ReadResponseAsync<TResponse>(response, cancellationToken);
    }
    catch (Exception ex)
    {
      return Fail<TResponse>(ex.Message);
    }
  }

  public async Task<RequestResponseDTO<TResponse>> PatchAsync<TRequest, TResponse>(
    string url,
    TRequest request,
    CancellationToken cancellationToken = default
  )
  {
    try
    {
      using var response = await httpClient.PatchAsJsonAsync(url, request, cancellationToken);

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
      using var response = await httpClient.DeleteAsync(url, cancellationToken);

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
    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
      return Fail<T>("Your session has expired. Please sign in again.");
    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
      return Fail<T>("You do not have permission to perform this action.");
    if (response.IsSuccessStatusCode)
    {
      try
      {
        using var stream = new ResponsiveReadStream(await response.Content.ReadAsStreamAsync(cancellationToken));
        return await System.Text.Json.JsonSerializer.DeserializeAsync<RequestResponseDTO<T>>(stream, ResponseOptions, cancellationToken)
          ?? Fail<T>("The server returned an empty response.");
      }
      catch (System.Text.Json.JsonException)
      {
        return Fail<T>("The server returned an invalid response.");
      }
    }
    var content = await response.Content.ReadAsStringAsync(cancellationToken);
    if (string.IsNullOrWhiteSpace(content))
      return Fail<T>($"The server returned {(int)response.StatusCode} without a response.");
    try
    {
      using var json = System.Text.Json.JsonDocument.Parse(content);
      if (json.RootElement.TryGetProperty("success", out _))
      {
        var result = System.Text.Json.JsonSerializer.Deserialize<RequestResponseDTO<T>>(content,
          ResponseOptions);
        if (result is not null && (response.IsSuccessStatusCode || !result.Success)) return result;
      }
      if (json.RootElement.TryGetProperty("title", out var title))
        return Fail<T>(title.GetString() ?? "The request failed.");
    }
    catch (System.Text.Json.JsonException) { }
    return Fail<T>($"The request failed (HTTP {(int)response.StatusCode}). Please try again.");
  }

  private static RequestResponseDTO<T> Fail<T>(string error)
  {
    return new RequestResponseDTO<T> { Success = false, Errors = [error] };
  }
}
