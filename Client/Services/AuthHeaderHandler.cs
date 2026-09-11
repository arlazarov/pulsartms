using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Client.Models.Auth;

namespace Client.Services;

public class AuthHeaderHandler(
  TokenStorageService tokenStorage,
  AppAuthenticationStateProvider authenticationStateProvider
) : DelegatingHandler
{
  internal static readonly HttpRequestOptionsKey<Guid> RequiredSession = new("amftms.required-session");
  private readonly SemaphoreSlim refreshGate = new(1, 1);
  protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken
  )
  {
    if (request.RequestUri?.AbsolutePath.EndsWith("/api/auth/login") == true)
    {
      request.Headers.Authorization = null;
      return await base.SendAsync(request, cancellationToken);
    }
    var session = await tokenStorage.GetSessionAsync();
    if (tokenStorage.SessionChanged)
    {
      authenticationStateProvider.NotifyUserLogout();
      throw new HttpRequestException("Your sign-in changed in another tab. Reload this tab or sign in again.");
    }
    if (request.Options.TryGetValue(RequiredSession, out var expectedSession) && session?.Id != expectedSession)
      return new(HttpStatusCode.Unauthorized) { RequestMessage = request };
    var accessToken = session?.AccessToken;

    if (!string.IsNullOrWhiteSpace(accessToken))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    var response = await base.SendAsync(request, cancellationToken);

    if (response.StatusCode != HttpStatusCode.Unauthorized)
      return response;

    if (
      request.RequestUri?.AbsolutePath.EndsWith("/api/auth/refresh") == true
    )
    {
      return response;
    }

    if (session is null) return response;
    TokenStorageService.Session? retrySession;
    await refreshGate.WaitAsync(cancellationToken);
    try
    {
      retrySession = await tokenStorage.GetSessionAsync();
      // A request started by one login must never be replayed as another user.
      if (retrySession?.Id != session.Id) return response;
      if (retrySession == session)
      {
        var refreshToken = session.RefreshToken;
        var refreshed = string.IsNullOrWhiteSpace(refreshToken) ? null
          : await RefreshAsync(request.RequestUri!, refreshToken, cancellationToken);
        if (refreshed is null)
        {
          if (await tokenStorage.ReplaceAsync(session, null))
            authenticationStateProvider.NotifyUserLogout();
          return response;
        }
        retrySession = session with { AccessToken = refreshed.AccessToken, RefreshToken = refreshed.RefreshToken };
        if (!await tokenStorage.ReplaceAsync(session, retrySession)) return response;
      }
      if (string.IsNullOrWhiteSpace(retrySession.AccessToken)) return response;
    }
    catch
    {
      response.Dispose();
      throw;
    }
    finally { refreshGate.Release(); }

    response.Dispose();
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", retrySession.AccessToken);

    var retriedResponse = await base.SendAsync(request, cancellationToken);
    if (retriedResponse.StatusCode == HttpStatusCode.Unauthorized)
    {
      await refreshGate.WaitAsync(cancellationToken);
      try
      {
        if (await tokenStorage.ReplaceAsync(retrySession, null))
        {
          authenticationStateProvider.NotifyUserLogout();
        }
      }
      finally { refreshGate.Release(); }
    }
    return retriedResponse;
  }

  private async Task<AuthResponse?> RefreshAsync(
    Uri requestUri,
    string refreshToken,
    CancellationToken cancellationToken
  )
  {
    using var request = new HttpRequestMessage(HttpMethod.Post,
      new Uri(requestUri, "/api/auth/refresh"))
    {
      Content = JsonContent.Create(new { RefreshToken = refreshToken })
    };
    using var response = await base.SendAsync(request, cancellationToken);

    if (response.StatusCode == HttpStatusCode.Unauthorized)
      return null;

    response.EnsureSuccessStatusCode();

    var refreshed = await response.Content.ReadFromJsonAsync<AuthResponse>(
      cancellationToken: cancellationToken
    );
    if (refreshed is not null && (string.IsNullOrWhiteSpace(refreshed.AccessToken) || string.IsNullOrWhiteSpace(refreshed.RefreshToken)))
      throw new System.Text.Json.JsonException("The refresh response did not contain valid tokens.");
    return refreshed;
  }
}
