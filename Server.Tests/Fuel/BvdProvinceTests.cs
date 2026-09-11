using Infrastructure.Integrations.Bvd;
using System.Text;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public sealed class BvdProvinceTests
{
  [Theory]
  [InlineData("STATE", "TX")]
  [InlineData("PROV", "ON")]
  [InlineData("PROVINCE", "QC")]
  [InlineData(" prov ", " bc ")]
  [InlineData("STATE,PROV", ",AB")]
  [InlineData("STATE,PROV", "ON,ON")]
  public void ReadsRegionWithoutChangingPrices(string headers, string values)
  {
    var csv = $",,,,,,,2026-09-09\nSITE,NAME,CITY,{headers},RETAIL PRICE,YOUR PRICE\n1,Station,Town,{values},1.799,1.459\n";
    var row = Assert.Single(BvdFuelCsvParser.Parse(Encoding.UTF8.GetBytes(csv)).Rows);
    Assert.Equal(values.Split(',').Last().Trim().ToUpperInvariant(), row.State);
    Assert.Equal(1.799m, row.RetailPrice);
    Assert.Equal(1.459m, row.DiscountPrice);
  }

  [Theory]
  [InlineData("STATE", "")]
  [InlineData("REGION", "ON")]
  [InlineData("STATE,PROV", "ON,QC")]
  public void MissingOrConflictingRegionCannotSilentlyEraseSavedProvince(string headers, string values)
  {
    var csv = $",,,,,,,2026-09-09\nSITE,NAME,CITY,{headers},RETAIL PRICE,YOUR PRICE\n1,Station,Town,{values},1.799,1.459\n";
    Assert.Throws<FormatException>(() => BvdFuelCsvParser.Parse(Encoding.UTF8.GetBytes(csv)));
  }
}
