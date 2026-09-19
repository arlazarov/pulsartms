using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.FleetMap;

public partial class LoadReference
{
  [Parameter]
  public int LoadNumber { get; set; }

  [Parameter]
  public string OrderNumber { get; set; } = "";

  [Parameter]
  public EventCallback<string> OnCopy { get; set; }

  private Task CopyLoadAsync() =>
    OnCopy.InvokeAsync(LoadNumber.ToString(CultureInfo.InvariantCulture));

  private Task CopyOrderAsync() => OnCopy.InvokeAsync(OrderNumber);
}
