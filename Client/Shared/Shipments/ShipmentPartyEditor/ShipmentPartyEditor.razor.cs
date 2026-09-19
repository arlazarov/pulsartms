using Client.Models.DTO.Addresses;
using Client.Models.DTO.Shipments;
using Microsoft.AspNetCore.Components;

namespace Client.Shared.Shipments.ShipmentPartyEditor;

public partial class ShipmentPartyEditor
{
  [Parameter, EditorRequired]
  public ShipmentParty Value { get; set; } = new();

  [Parameter]
  public string Title { get; set; } = "";

  [Parameter]
  public bool Disabled { get; set; }

  [Parameter]
  public EventCallback Changed { get; set; }

  private async Task ApplyAddress(PostalAddress address)
  {
    if (Disabled)
      return;
    Value.AddressLine1 = address.Address;
    Value.AddressLine2 = "";
    Value.City = address.City;
    Value.Region = address.Region;
    Value.Country = address.Country;
    Value.PostalCode = address.PostalCode;
    await Changed.InvokeAsync();
  }

  private Task NotifyChanged() => Changed.InvokeAsync();
}
