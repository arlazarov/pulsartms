using Microsoft.AspNetCore.Components;

namespace Client.Components.DataTable;

public partial class DataTablePagination : DataTable
{
  [Parameter]
  public int Page { get; set; }

  [Parameter]
  public int TotalPages { get; set; }

  [Parameter]
  public bool HasPreviousPage { get; set; }

  [Parameter]
  public bool HasNextPage { get; set; }

  [Parameter]
  public EventCallback<int> OnPageChanged { get; set; }

  protected async Task PreviousPageAsync()
  {
    if (!HasPreviousPage)
      return;

    await OnPageChanged.InvokeAsync(Page - 1);
  }

  protected async Task NextPageAsync()
  {
    if (!HasNextPage)
      return;

    await OnPageChanged.InvokeAsync(Page + 1);
  }
}
