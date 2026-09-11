using Infrastructure.Integrations.Bvd;
using System.Text;
namespace Server.Tests.Fuel;
[Trait("Category", "Fuel")]
public class BvdDateRangeTests
{
  [Theory]
  [InlineData("2026-09-06", 6)]
  [InlineData("2026-09-06 to 2026-09-09", 9)]
  public void PreservesPriceValidity(string date, int endDay)
  {
    var csv = $",,,,,,,{date}\nSITE,NAME,CITY,STATE,RETAIL PRICE,YOUR PRICE\n1,Station,City,CA,5.5,5.1\n";
    var parsed = BvdFuelCsvParser.Parse(Encoding.UTF8.GetBytes(csv));
    Assert.Equal(new DateOnly(2026, 9, 6), parsed.EffectiveDate);
    Assert.Equal(new DateOnly(2026, 9, endDay), parsed.EffectiveTo);
    Assert.Single(parsed.Rows);
  }
  [Fact]
  public void RejectsReversedRange() => Assert.Throws<FormatException>(() =>
    BvdFuelCsvParser.Parse(Encoding.UTF8.GetBytes(",,,,,,,2026-09-09 to 2026-09-06\n")));
}
