using System.Globalization;
using Client.Shared;
using Client.Shared.Dispatch;

namespace Client.Tests.Dispatch;

[Trait("Category", "Dispatch")]
[Trait("Kind", "Unit")]
public sealed class LoadNumberDisplayTests
{
  [Theory]
  [InlineData(1373, "AMF", "AMF1373")]
  [InlineData(1373, "TMS-", "TMS-1373")]
  [InlineData(1373, "", "1373")]
  [InlineData(1373, null, "1373")]
  [InlineData(null, "AMF", "—")]
  [InlineData(null, "", "—")]
  [InlineData(null, null, "—")]
  [InlineData(0, "AMF", "AMF0")]
  public void FormatsOnlyTheNumberAndProvidedPrefix(
    int? number,
    string? prefix,
    string expected
  )
  {
    Assert.Equal(expected, LoadNumberDisplay.Format(number, prefix));
  }

  [Fact]
  public void NumberFormattingDoesNotDependOnTheBrowserCulture()
  {
    var previous = CultureInfo.CurrentCulture;
    try
    {
      var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
      culture.NumberFormat.NegativeSign = "~";
      CultureInfo.CurrentCulture = culture;

      Assert.Equal("AMF-1373", LoadNumberDisplay.Format(-1373, "AMF"));
      Assert.Equal("AMF1234567", LoadNumberDisplay.Format(1234567, "AMF"));
    }
    finally
    {
      CultureInfo.CurrentCulture = previous;
    }
  }
}
