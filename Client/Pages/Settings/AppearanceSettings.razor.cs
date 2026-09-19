using Client.Shared.Appearance.AppearanceProvider;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Settings;

public partial class AppearanceSettings
{
  [CascadingParameter]
  private AppearanceProvider? Appearance { get; set; }

  private Task ChangeTemperatureAsync(string value) =>
    Appearance is null
      ? Task.CompletedTask
      : Appearance.SaveAsync(Appearance.Theme, temperature: value);

  private Task ChangeDistanceAsync(string value) =>
    Appearance is null
      ? Task.CompletedTask
      : Appearance.SaveAsync(Appearance.Theme, distance: value);
}
