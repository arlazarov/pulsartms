using Microsoft.AspNetCore.Components;
namespace Client.Shared.ActionIcon;
public partial class ActionIcon
{
  [Parameter] public string Kind { get; set; } = "follow";
}
