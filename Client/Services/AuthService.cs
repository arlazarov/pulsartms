using System.Net.Http.Json;
using Client.Models.Auth;

namespace Client.Services;

public class AuthService(HttpClient httpClient, TokenStorageService tokenStorage, PlanningDisplayCache planningCache)
{
  public async Task<AuthResponse?> LoginAsync(
    string email,
    string password,
    CancellationToken cancellationToken = default
  )
  {
    var request = new LoginRequest(email, password);

    using var response = await httpClient.PostAsJsonAsync("api/auth/login", request, cancellationToken);

    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
      return null;

    response.EnsureSuccessStatusCode();

    var authResponse = await response.Content.ReadFromJsonAsync<AuthResponse>(
      cancellationToken: cancellationToken
    );

    if (authResponse is null)
      return null;
    if (string.IsNullOrWhiteSpace(authResponse.AccessToken) || string.IsNullOrWhiteSpace(authResponse.RefreshToken))
      throw new System.Text.Json.JsonException("The sign-in response did not contain valid tokens.");

    planningCache.Clear();
    await tokenStorage.SetTokensAsync(authResponse.AccessToken, authResponse.RefreshToken);

    return authResponse;
  }

  public async Task LogoutAsync(CancellationToken cancellationToken = default)
  {
    var session = await tokenStorage.GetSessionAsync();
    if (session is null) return;
    try
    {
      using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
      request.Options.Set(AuthHeaderHandler.RequiredSession, session.Id);
      using var response = await httpClient.SendAsync(request, cancellationToken);
      response.EnsureSuccessStatusCode();
    }
    finally
    {
      if (await tokenStorage.ClearSessionAsync(session.Id)) planningCache.Clear();
    }
  }
}
