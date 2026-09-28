using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Client.Layout;

public partial class Sidebar
{
  private bool MenuOpen;
  private ElementReference MenuToggle;

  private void ToggleMenu() => MenuOpen = !MenuOpen;

  private void CloseMenu() => MenuOpen = false;

  private async Task HandleMenuKeyAsync(KeyboardEventArgs args)
  {
    if (args.Key != "Escape" || !MenuOpen)
      return;
    CloseMenu();
    await MenuToggle.FocusAsync();
  }
}
