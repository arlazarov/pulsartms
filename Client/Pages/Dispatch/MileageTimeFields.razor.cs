using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class MileageTimeFields
{
  [Parameter, EditorRequired]
  public MileageTimeDraft Draft { get; set; } = default!;

  [Parameter, EditorRequired]
  public string Id { get; set; } = "";

  [Parameter]
  public string Label { get; set; } = "Actual";

  [Parameter]
  public bool Disabled { get; set; }
}
