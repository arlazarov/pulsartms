using Client.Models.DTO.Dispatch.Workspace;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchAssignmentReview
{
  [Parameter, EditorRequired]
  public DispatchAssignmentProposal Source { get; set; } = new();

  [Parameter]
  public string? Reason { get; set; }

  [Parameter]
  public IReadOnlyList<DispatchAcceptedAssignment> Accepted { get; set; } = [];

  private static string Place(string value) =>
    string.IsNullOrWhiteSpace(value) ? "Unnamed stop" : value;
}
