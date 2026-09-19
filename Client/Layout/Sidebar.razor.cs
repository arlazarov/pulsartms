using System.Globalization;
using System.Security.Claims;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Layout;

public partial class Sidebar
{
  [Inject]
  protected AuthService AuthService { get; set; } = default!;

  [Inject]
  protected AppAuthenticationStateProvider AuthenticationStateProvider { get; set; } =
    default!;

  [Inject]
  protected NavigationManager Navigation { get; set; } = default!;

  private bool MenuOpen;
  private ElementReference MenuToggle;
  private bool AccountOpen;
  private ElementReference AccountToggle;

  private static string AccountName(ClaimsPrincipal user) =>
    string.IsNullOrWhiteSpace(user.Identity?.Name)
      ? "Account"
      : user.Identity.Name;

  private static string AccountInitials(ClaimsPrincipal user) =>
    string.Concat(
        AccountName(user)
          .Split(' ', StringSplitOptions.RemoveEmptyEntries)
          .Take(2)
          .Select(part => StringInfo.GetNextTextElement(part))
      )
      .ToUpperInvariant();

  private void ToggleMenu() => MenuOpen = !MenuOpen;

  private void ToggleAccount() => AccountOpen = !AccountOpen;

  private void CloseMenu()
  {
    MenuOpen = false;
    AccountOpen = false;
  }

  private async Task HandleMenuKeyAsync(KeyboardEventArgs args)
  {
    if (args.Key != "Escape")
      return;
    if (AccountOpen)
    {
      AccountOpen = false;
      await AccountToggle.FocusAsync();
      return;
    }
    if (!MenuOpen)
      return;
    CloseMenu();
    await MenuToggle.FocusAsync();
  }

  protected async Task LogoutAsync()
  {
    try
    {
      await AuthService.LogoutAsync();
    }
    catch (Exception ex)
      when (ex is HttpRequestException or OperationCanceledException) { }
    finally
    {
      if (!await AuthenticationStateProvider.NotifyCurrentSessionAsync())
        Navigation.NavigateTo("/login");
    }
  }
}
