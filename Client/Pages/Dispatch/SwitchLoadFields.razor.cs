using Client.Models.DTO.Execution;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class SwitchLoadFields
{
  [Parameter, EditorRequired]
  public SwitchLoadDraft Draft { get; set; } = default!;

  [Parameter, EditorRequired]
  public SwitchWorkspace Workspace { get; set; } = default!;

  [Parameter]
  public bool Completed { get; set; }
  private string Id => $"switch-load-{Draft.Source.DispatchId}";
  private bool Confirmed => Draft.Source.OutgoingLegId.HasValue;

  private static string Visit(SwitchVisitOption visit) =>
    $"#{visit.Sequence} {visit.Job} · {visit.Name} {visit.Address}".Trim();
}
