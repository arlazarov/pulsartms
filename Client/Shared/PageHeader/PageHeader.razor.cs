using Microsoft.AspNetCore.Components;

namespace Client.Shared.PageHeader;

public partial class PageHeader
{
  [Parameter, EditorRequired] public string Title { get; set; } = string.Empty;
  [Parameter] public string? Description { get; set; }
  [Parameter] public RenderFragment? ChildContent { get; set; }
}
