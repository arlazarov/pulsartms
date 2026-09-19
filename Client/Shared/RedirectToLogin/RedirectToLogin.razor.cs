using Microsoft.AspNetCore.Components;

namespace Client.Shared.RedirectToLogin;

public partial class RedirectToLogin
{
  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  protected override void OnInitialized()
  {
    Navigation.NavigateTo("/login", replace: true);
  }
}
