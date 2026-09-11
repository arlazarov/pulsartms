using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Auth;

public partial class Login : IDisposable
{
  private bool _disposed;
  protected bool IsCheckingSession { get; private set; } = true;

  [Inject]
  protected AuthService AuthService { get; set; } = default!;

  [Inject]
  protected AppAuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

  [Inject]
  protected NavigationManager Navigation { get; set; } = default!;

  protected LoginModel Model { get; } = new();

  protected bool IsLoading { get; set; }

  protected string? Error { get; set; }

  protected override async Task OnInitializedAsync()
  {
    var loginUri = Navigation.Uri;
    var state = await AuthenticationStateProvider.GetAuthenticationStateAsync();
    if (_disposed || Navigation.Uri != loginUri) return;
    if (state.User.Identity?.IsAuthenticated == true)
      Navigation.NavigateTo("/fleet/map", replace: true);
    else
      IsCheckingSession = false;
  }

  public void Dispose() => _disposed = true;

  protected async Task LoginAsync()
  {
    IsLoading = true;
    Error = null;

    try
    {
      var response = await AuthService.LoginAsync(Model.Email, Model.Password);
      if (response is null)
      {
        Error = "Invalid email or password.";
        return;
      }
      AuthenticationStateProvider.NotifyUserAuthentication();
      Navigation.NavigateTo("/fleet/map");
    }
    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
    {
      Error = "Could not reach the server. Please try again.";
    }
    catch (Microsoft.JSInterop.JSException)
    {
      Error = "Secure sign-in storage is unavailable. Use a current browser over HTTPS or localhost.";
    }
    finally { IsLoading = false; }
  }

  protected sealed class LoginModel
  {
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
  }
}
