using Client.Models.DTO.Execution;
using Microsoft.AspNetCore.Components;

namespace Client.Pages.Dispatch;

public partial class SwitchResourceSelect
{
  [Parameter]
  public string Id { get; set; } = "";

  [Parameter]
  public string Label { get; set; } = "";

  [Parameter]
  public Guid? Value { get; set; }

  [Parameter]
  public EventCallback<Guid?> ValueChanged { get; set; }

  [Parameter]
  public IReadOnlyList<SwitchResourceOption> Options { get; set; } = [];

  [Parameter]
  public bool Disabled { get; set; }

  private Guid? Selected { get; set; }

  protected override void OnParametersSet() => Selected = Value;

  private Task NotifyAsync() => ValueChanged.InvokeAsync(Selected);
}
