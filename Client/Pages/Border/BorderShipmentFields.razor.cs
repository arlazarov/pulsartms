using Client.Models.DTO.Border;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Border;

public partial class BorderShipmentFields
{
  [Parameter, EditorRequired]
  public BorderShipment Value { get; set; } = new();

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  [Parameter]
  public List<BorderEquipment> Equipment { get; set; } = [];

  private Task NotifyChanged() => Changed.InvokeAsync();
}
