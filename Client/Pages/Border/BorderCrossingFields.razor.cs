using Client.Models.DTO.Border;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Border;

public partial class BorderCrossingFields
{
  [Parameter, EditorRequired]
  public BorderCrossing Value { get; set; } = new();

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  [Parameter]
  public List<BorderPort> Ports { get; set; } = [];

  private Task DestinationChanged()
  {
    Value.PortOfEntry = "";
    return NotifyChanged();
  }

  private Task NotifyChanged() => Changed.InvokeAsync();
}
