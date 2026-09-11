using Microsoft.AspNetCore.Components;

namespace Client.Components.Form;

public partial class FormField
{
  [Parameter]
  public string Label { get; set; } = string.Empty;

  [Parameter]
  public string For { get; set; } = string.Empty;

  [Parameter]
  public bool HasError { get; set; }

  [Parameter]
  public RenderFragment? ChildContent { get; set; }
}
