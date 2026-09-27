using System.Text;
using Infrastructure.Integrations.Bvd;
using Infrastructure.Integrations.Google.Gmail;

namespace Server.Tests.Fuel;

[Trait("Category", "Fuel")]
[Trait("Kind", "Unit")]
public class BvdDateRangeTests
{
  [Theory]
  [InlineData("2026-09-06", 6)]
  [InlineData("2026-09-06 to 2026-09-09", 9)]
  public void PreservesPriceValidity(string date, int endDay)
  {
    var csv =
      $",,,,,,,{date}\nSITE,NAME,CITY,STATE,RETAIL PRICE,YOUR PRICE\n1,Station,City,CA,5.5,5.1\n";
    var parsed = BvdFuelCsvParser.Parse(Encoding.UTF8.GetBytes(csv));
    Assert.Equal(new DateOnly(2026, 9, 6), parsed.EffectiveDate);
    Assert.Equal(new DateOnly(2026, 9, endDay), parsed.EffectiveTo);
    Assert.Single(parsed.Rows);
  }

  [Fact]
  public void RejectsReversedRange() =>
    Assert.Throws<FormatException>(
      () =>
        BvdFuelCsvParser.Parse(
          Encoding.UTF8.GetBytes(",,,,,,,2026-09-09 to 2026-09-06\n")
        )
    );

  // Audit F20: a file that cannot be read marks its own attachment, and
  // the run goes on; a file for no known currency is not an import.
  [Theory]
  [InlineData(",,,,,,,not a date\n", true)]
  [InlineData(",,,,,,,2026-09-09 to 2026-09-06\n", true)]
  [InlineData(
    ",,,,,,,2026-09-06\nSITE,NAME,CITY,STATE,RETAIL PRICE,YOUR PRICE\n"
      + "1,Station,City,CA,5.5,5.1\n",
    false
  )]
  public void AnAttachmentThatCannotBeReadIsMarkedNotThrown(
    string csv,
    bool unreadable
  )
  {
    var read = BvdFuelDiscountProvider.Read(
      new()
      {
        MessageId = "m",
        FileName = "prices USD.csv",
        Content = Encoding.UTF8.GetBytes(csv),
      }
    );

    Assert.Equal(unreadable, read!.Unreadable);
    Assert.Equal(unreadable ? 0 : 1, read.Rows.Count);
    Assert.Null(
      BvdFuelDiscountProvider.Read(
        new() { MessageId = "m", FileName = "x.csv" }
      )
    );
  }

  [Fact]
  public void TheMailboxIsAskedFromAGivenTime()
  {
    var query = GmailAttachmentService.Query(
      new DateTime(2026, 9, 25, 21, 0, 0, DateTimeKind.Utc)
    );

    Assert.Equal(
      "label:fleet-bvd-fuel after:1790370000 has:attachment filename:csv",
      query
    );
  }
}
