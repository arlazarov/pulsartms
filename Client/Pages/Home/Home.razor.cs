using Client.Services;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Home;

public partial class Home
{
  [Inject]
  protected AuthService AuthService { get; set; } = default!;

  [Inject]
  protected AppAuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

  [Inject]
  protected NavigationManager Navigation { get; set; } = default!;

  protected override void OnInitialized()
  {
    Navigation.NavigateTo("/fleet/map", replace: true);
  }

  protected async Task LogoutAsync()
  {
    await AuthService.LogoutAsync();

    if (!await AuthenticationStateProvider.NotifyCurrentSessionAsync())
      Navigation.NavigateTo("/login");
  }
}
