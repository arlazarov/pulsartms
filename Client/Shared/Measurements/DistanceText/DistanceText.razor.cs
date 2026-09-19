using Client.Models;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Measurements.DistanceText;

public partial class DistanceText
{
  [CascadingParameter]
  public DisplayUnits Units { get; set; } = DisplayUnits.Default;

  [Parameter]
  public double? Miles { get; set; }
}
