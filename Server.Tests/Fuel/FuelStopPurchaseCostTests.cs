using System.Text.Json;
using Application.Features.Routing.Models;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class FuelStopPurchaseCostTests
{
  [Theory]
  [InlineData("USD", "US gal", 5.50)]
  [InlineData("CAD", "L", 1.98)]
  public void CostUsesNormalizedCashPriceNotDisplayOrIftaPrice(
    string currency,
    string unit,
    double displayPrice
  )
  {
    var stop = new FuelPlanStop
    {
      BuyGallons = 160,
      CashUsdPerGallon = 5.50,
      EconomicUsdPerGallon = 4.90,
      YourPrice = displayPrice,
      EconomicPrice = 1.70,
      Currency = currency,
      Unit = unit,
    };

    Assert.Equal(880, stop.PurchaseCostUsd);
  }

  [Fact]
  public void ProjectedQuantityUpdatesCostWithoutRetainingAnOldTotal()
  {
    var stop = new FuelPlanStop
    {
      BuyGallons = 160,
      CashUsdPerGallon = 5.50,
      FillToTarget = true,
    };
    Assert.Equal(880, stop.PurchaseCostUsd);

    stop.BuyGallons = 30;
    stop.FillToTarget = false;
    Assert.Equal(165, stop.PurchaseCostUsd);

    stop.BuyGallons = 0;
    Assert.Equal(0, stop.PurchaseCostUsd);
  }

  [Theory]
  [InlineData(-1, 5)]
  [InlineData(double.NaN, 5)]
  [InlineData(double.PositiveInfinity, 5)]
  [InlineData(30, 0)]
  [InlineData(30, -5)]
  [InlineData(30, double.NaN)]
  [InlineData(30, double.PositiveInfinity)]
  [InlineData(double.MaxValue, 2)]
  public void MissingOrInvalidPricingDoesNotPublishAMisleadingCost(
    double gallons,
    double price
  )
  {
    var stop = new FuelPlanStop
    {
      BuyGallons = gallons,
      CashUsdPerGallon = price,
    };
    Assert.Null(stop.PurchaseCostUsd);
  }

  [Fact]
  public void JsonPublishesComputedUsdCostAndDoesNotTrustAnOldSerializedTotal()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var stop = JsonSerializer.Deserialize<FuelPlanStop>(
      """{"buyGallons":30,"cashUsdPerGallon":5.5,"purchaseCostUsd":999}""",
      options
    )!;

    Assert.Equal(165, stop.PurchaseCostUsd);
    using var payload = JsonDocument.Parse(
      JsonSerializer.Serialize(stop, options)
    );
    Assert.Equal(
      165,
      payload.RootElement.GetProperty("purchaseCostUsd").GetDouble()
    );
  }
}
