using Microsoft.AspNetCore.Components;

namespace Client.Components.Form;

public partial class Form
{
  [Parameter, EditorRequired]
  public object Model { get; set; } = default!;

  [Parameter]
  public RenderFragment? ChildContent { get; set; }

  [Parameter]
  public EventCallback OnSubmit { get; set; }

  [Parameter]
  public EventCallback OnCancel { get; set; }

  [Parameter]
  public bool IsSaving { get; set; }

  [Parameter]
  public string SubmitText { get; set; } = "Save";

  [Parameter]
  public string SavingText { get; set; } = "Saving...";

  [Parameter]
  public Dictionary<string, List<string>> Errors { get; set; } = [];

  protected Task SubmitAsync()
  {
    return OnSubmit.InvokeAsync();
  }

  protected Task CancelAsync()
  {
    return OnCancel.InvokeAsync();
  }
}
