using Client.Models.DTO.Dispatch.Workspace;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchAssignmentResources
{
  [Parameter, EditorRequired]
  public DispatchResourceProposal Resources { get; set; } =
    new("", "", "", "", null, null, null, null);

  [Parameter]
  public bool Proposed { get; set; }

  private (string Label, string Name, Guid? Id)[] Fields =>
    [
      ("Truck", Resources.Truck, Resources.TruckId),
      ("Trailer", Resources.Trailer, Resources.TrailerId),
      ("Driver", Resources.Driver, Resources.DriverId),
      ("Co-driver", Resources.CoDriver, Resources.CoDriverId),
    ];
}
