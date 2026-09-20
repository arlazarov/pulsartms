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

  // How the table reads where there is no room for columns: "cards" gives
  // each row a card of its own, "stacked" keeps the rows and drops the
  // heading. The stylesheet that owns the table owns both; a page asks for
  // one instead of restyling the rows from its own sheet.
  [Parameter]
  public string Reading { get; set; } = string.Empty;
  private string ReadingClass =>
    Reading.Length > 0 ? $"data-table data-table--{Reading}" : "data-table";
}
