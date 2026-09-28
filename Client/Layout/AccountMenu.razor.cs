using System.Globalization;
using System.Security.Claims;
using Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Layout;

public partial class AccountMenu
{
  [Inject]
  private AuthService AuthService { get; set; } = default!;

  [Inject]
  private AppAuthenticationStateProvider AuthenticationStateProvider { get; set; } =
    default!;

  [Inject]
  private NavigationManager Navigation { get; set; } = default!;

  [Parameter]
  public string Block { get; set; } = "sidebar";

  // Told when an action leaves the page, so a holding menu can close.
  [Parameter]
  public EventCallback Navigated { get; set; }

  // Told of an Escape the closed menu does not use, so a holding menu can
  // close. Keys never bubble past the menu: a conditional stop let the
  // Escape that closed the actions also close the phone's menu.
  [Parameter]
  public EventCallback Escaped { get; set; }

  private bool Open;
  private ElementReference _toggle;

  private static string Name(ClaimsPrincipal user) =>
    string.IsNullOrWhiteSpace(user.Identity?.Name)
      ? "Account"
      : user.Identity.Name;

  private static string Initials(ClaimsPrincipal user) =>
    string.Concat(
        Name(user)
          .Split(' ', StringSplitOptions.RemoveEmptyEntries)
          .Take(2)
          .Select(part => StringInfo.GetNextTextElement(part))
      )
      .ToUpperInvariant();

  private void Toggle() => Open = !Open;

  private async Task HandleKeyAsync(KeyboardEventArgs args)
  {
    if (args.Key != "Escape")
      return;
    if (!Open)
    {
      await Escaped.InvokeAsync();
      return;
    }
    Open = false;
    await _toggle.FocusAsync();
  }

  private Task LeaveAsync()
  {
    Open = false;
    return Navigated.InvokeAsync();
  }

  private async Task LogoutAsync()
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
