using Microsoft.AspNetCore.Components;

namespace Client.Pages.FleetMap;

public partial class TruckLocationLine
{
  [Parameter]
  public string Label { get; set; } = "";

  [Parameter]
  public string? Address { get; set; }

  [Parameter]
  public EventCallback<string> OnCopy { get; set; }
}
