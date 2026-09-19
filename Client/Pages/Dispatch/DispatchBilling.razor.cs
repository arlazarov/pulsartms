using Client.Models.DTO.Dispatch;
using Client.Models.DTO.Dispatch.Workspace;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchBilling
{
  [Parameter, EditorRequired]
  public DispatchWorkspaceMetadata Metadata { get; set; } = new();

  [Parameter, EditorRequired]
  public DispatchResponse Load { get; set; } = new();

  [Parameter]
  public bool CanEdit { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  private Task NotifyChanged() => Changed.InvokeAsync();
}
