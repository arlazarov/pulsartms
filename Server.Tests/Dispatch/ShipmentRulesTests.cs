using Application.Features.Shipments.Models;
using Application.Features.Shipments.Services;

namespace Server.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class ShipmentRulesTests
{
  [Fact]
  public void DomesticShipmentHasNoBorderRequirements()
  {
    var shipment = new Shipment
    {
      Id = Guid.NewGuid(),
      LoadId = Guid.NewGuid(),
    };
    Assert.Null(ShipmentRules.Validate(shipment));
    Assert.DoesNotContain(
      ShipmentRules.Missing(shipment),
      x =>
        x.Field
          is "Procedure"
            or "DestinationCountry"
            or "ParsNumber"
            or "PapsNumber"
    );
  }
}
