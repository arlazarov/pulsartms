using Client.Models.DTO;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Dispatch.LoadNumber;

public partial class LoadNumber
{
  [CascadingParameter]
  public DispatchSettingsState? DisplaySettings { get; set; }

  [Parameter]
  public int? Value { get; set; }
  private string Text =>
    LoadNumberDisplay.Format(Value, DisplaySettings?.LoadNumberPrefix);
}
