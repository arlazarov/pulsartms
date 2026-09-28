using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Layout;

public partial class Sidebar
{
  private bool MenuOpen;
  private ElementReference MenuToggle;

  private void ToggleMenu() => MenuOpen = !MenuOpen;

  private void CloseMenu() => MenuOpen = false;

  private Task HandleMenuKeyAsync(KeyboardEventArgs args) =>
    args.Key == "Escape" ? CloseMenuAndFocusAsync() : Task.CompletedTask;

  private async Task CloseMenuAndFocusAsync()
  {
    if (!MenuOpen)
      return;
    CloseMenu();
    await MenuToggle.FocusAsync();
  }
}
