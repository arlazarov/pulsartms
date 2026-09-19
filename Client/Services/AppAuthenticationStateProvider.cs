using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Client.Services;

public class AppAuthenticationStateProvider(
  TokenStorageService tokenStorage,
  IServiceProvider services
) : AuthenticationStateProvider
{
  private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

  public override async Task<AuthenticationState> GetAuthenticationStateAsync()
  {
    try
    {
      var session = await tokenStorage.GetSessionAsync();
      if (string.IsNullOrWhiteSpace(session?.AccessToken))
        return new AuthenticationState(Anonymous);
      var profile = await services
        .GetRequiredService<HttpClient>()
        .GetFromJsonAsync<CurrentUser>("api/auth/me");
      if (
        profile is null
        || (await tokenStorage.GetSessionAsync())?.Id != session.Id
      )
        return new AuthenticationState(Anonymous);
      var claims = new List<Claim>
      {
        new(ClaimTypes.NameIdentifier, profile.Id.ToString()),
        new(ClaimTypes.Name, profile.Name),
        new(ClaimTypes.Email, profile.Email),
      };
      claims.Add(new(ClaimTypes.Role, profile.IsAdmin ? "Admin" : "Dispatch"));
      return new AuthenticationState(
        new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"))
      );
    }
    catch (Exception ex)
      when (ex
          is HttpRequestException
            or OperationCanceledException
            or JsonException
            or JSException
      )
    {
      return new AuthenticationState(Anonymous);
    }
  }

  public void NotifyUserAuthentication() =>
    NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

  public void NotifyUserLogout() =>
    NotifyAuthenticationStateChanged(
      Task.FromResult(new AuthenticationState(Anonymous))
    );

  public async Task<bool> NotifyCurrentSessionAsync()
  {
    try
    {
      if (await tokenStorage.GetSessionAsync() is not null)
      {
        NotifyUserAuthentication();
        return true;
      }
    }
    catch (JSException) { }
    NotifyUserLogout();
    return false;
  }

  private sealed record CurrentUser(
    Guid Id,
    string Name,
    string Email,
    bool IsAdmin
  );
}
