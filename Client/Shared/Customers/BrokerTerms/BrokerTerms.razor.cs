using Client.Models.DTO.Dispatch.Workspace;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Customers.BrokerTerms;

public partial class BrokerTerms
{
  [Parameter, EditorRequired]
  public BrokerPaymentTerms Terms { get; set; } = new();

  [Parameter]
  public bool CanEdit { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  private Task NotifyChanged() => Changed.InvokeAsync();
}
