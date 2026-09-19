using Client.Models.DTO.Mileage;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class DispatchMileageTotals
{
  [Parameter, EditorRequired]
  public string Title { get; set; } = "";

  [Parameter, EditorRequired]
  public MileageTotals Totals { get; set; } = default!;
}
