using Microsoft.AspNetCore.Components;

namespace Client.Components.DataTable;

public partial class DataTable
{
  [Parameter]
  public RenderFragment? Header { get; set; }

  [Parameter]
  public RenderFragment? Rows { get; set; }

  [Parameter]
  public string Columns { get; set; } = "1fr";
}
