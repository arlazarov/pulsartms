using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Application.Storage;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Integrations.Google.Drive;

// PulsR's Google client registration and access tokens. The registration is
// a deployment secret (GoogleDrive:ClientId, ClientSecret, RedirectUri, and
// PickerApiKey and AppId for the folder picker);
// a company's refresh token is its connection's protected secret. Access
// tokens are kept in memory per connection until shortly before expiry.
public sealed class GoogleDriveClient(IConfiguration configuration)
{
  public const string Scope = "https://www.googleapis.com/auth/drive.file";
  public const string TokenUrl = "https://oauth2.googleapis.com/token";
  public const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
  public const string UploadUrl =
    "https://www.googleapis.com/upload/drive/v3/files";

  private readonly ConcurrentDictionary<
    Guid,
    (string Token, DateTime Until)
  > tokens = new();

  public string? ClientId => Value("ClientId");
  public string? ClientSecret => Value("ClientSecret");
  public string? RedirectUri => Value("RedirectUri");

  // The Picker's browser key and the Cloud project number it belongs to.
  public string? PickerApiKey => Value("PickerApiKey");
  public string? AppId => Value("AppId");

  public bool PickerConfigured =>
    IsConfigured && PickerApiKey is not null && AppId is not null;

  public bool IsConfigured =>
    ClientId is not null
    && ClientSecret is not null
    && Uri.TryCreate(RedirectUri, UriKind.Absolute, out var uri)
    && uri.Scheme == Uri.UriSchemeHttps;

  public sealed record Secret(string RefreshToken);

  public static string Serialize(string refreshToken) =>
    JsonSerializer.Serialize(new Secret(refreshToken));

  public async Task<string> AccessTokenAsync(
    HttpClient http,
    StorageTarget target,
    CancellationToken ct
  )
  {
    if (
      tokens.TryGetValue(target.Connection, out var cached)
      && cached.Until > DateTime.UtcNow
    )
      return cached.Token;
    var refresh =
      Read(target.Secret)?.RefreshToken
      ?? throw new StorageUnavailableException("The connection has no grant.");
    var answer =
      await TokenAsync(
        http,
        [new("grant_type", "refresh_token"), new("refresh_token", refresh)],
        ct
      ) ?? throw new StorageUnavailableException("Google refused the grant.");
    tokens[target.Connection] = (answer.AccessToken, answer.Until);
    return answer.AccessToken;
  }

  public sealed record TokenAnswer(
    string AccessToken,
    string? RefreshToken,
    DateTime Until
  );

  // Null when Google refused; the response body is never surfaced.
  public async Task<TokenAnswer?> TokenAsync(
    HttpClient http,
    IEnumerable<KeyValuePair<string, string>> fields,
    CancellationToken ct
  )
  {
    if (!IsConfigured)
      return null;
    using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
    {
      Content = new FormUrlEncodedContent(
        [
          .. fields,
          new("client_id", ClientId!),
          new("client_secret", ClientSecret!),
        ]
      ),
    };
    string body;
    try
    {
      using var response = await http.SendAsync(request, ct);
      if (!response.IsSuccessStatusCode)
        return null;
      body = await response.Content.ReadAsStringAsync(ct);
    }
    catch (Exception ex)
      when (ex is HttpRequestException
        || ex is OperationCanceledException && !ct.IsCancellationRequested
      )
    {
      return null;
    }
    JsonDocument document;
    try
    {
      document = JsonDocument.Parse(body);
    }
    catch (JsonException)
    {
      return null;
    }
    using var parsed = document;
    var root = document.RootElement;
    if (
      !root.TryGetProperty("access_token", out var access)
      || access.GetString() is not { Length: > 0 } token
    )
      return null;
    var seconds =
      root.TryGetProperty("expires_in", out var expires)
      && expires.TryGetInt32(out var value)
        ? value
        : 300;
    return new(
      token,
      root.TryGetProperty("refresh_token", out var refresh)
        ? refresh.GetString()
        : null,
      DateTime.UtcNow.AddSeconds(Math.Max(0, seconds - 60))
    );
  }

  public void Remember(Guid connection, TokenAnswer answer) =>
    tokens[connection] = (answer.AccessToken, answer.Until);

  public static HttpRequestMessage Request(
    HttpMethod method,
    string url,
    string accessToken
  )
  {
    var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue(
      "Bearer",
      accessToken
    );
    return request;
  }

  // A lost connection is the provider being unavailable, not a server error.
  public static async Task<HttpResponseMessage> SendAsync(
    HttpClient http,
    HttpRequestMessage request,
    CancellationToken ct,
    HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead
  )
  {
    try
    {
      return await http.SendAsync(request, completion, ct);
    }
    catch (Exception ex)
      when (ex is HttpRequestException
        || ex is OperationCanceledException && !ct.IsCancellationRequested
      )
    {
      throw new StorageUnavailableException("Google Drive did not answer.");
    }
  }

  // Drive ids become part of request paths.
  public static bool IsId(string? id) =>
    id is { Length: >= 10 and <= 200 }
    && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

  public static string? Id(string json)
  {
    try
    {
      using var document = JsonDocument.Parse(json);
      return
        document.RootElement.TryGetProperty("id", out var id)
        && id.GetString() is { } value
        && IsId(value)
        ? value
        : null;
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static Secret? Read(string? json)
  {
    if (json is null)
      return null;
    try
    {
      return JsonSerializer.Deserialize<Secret>(json);
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private string? Value(string name) =>
    configuration[$"GoogleDrive:{name}"]?.Trim() is { Length: > 0 } value
      ? value
      : null;
}
