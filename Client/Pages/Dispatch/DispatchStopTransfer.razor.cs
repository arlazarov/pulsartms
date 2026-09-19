using Client.Models.DTO.Dispatch.Workspace;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchStopTransfer
{
  [Parameter, EditorRequired]
  public DispatchWorkspaceTransfer Transfer { get; set; } = default!;

  [Parameter]
  public Guid StopId { get; set; }

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public EventCallback<Guid> ManageRequested { get; set; }

  private string Heading =>
    Transfer.Kind == "drop_hook" ? "Trailer transfer" : "Resource change";
  private string Status =>
    Transfer.Status == "confirmed" ? "Confirmed" : "Planned";

  private static string Text(string? value) =>
    string.IsNullOrWhiteSpace(value) ? "—" : value;
}
