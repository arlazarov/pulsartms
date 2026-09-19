using System.Globalization;

namespace Application.Models;

public static class DecimalValue
{
  public static decimal? Normalize(decimal? value) =>
    value is { } number
      ? decimal.Parse(
        number.ToString("G29", CultureInfo.InvariantCulture),
        NumberStyles.Float,
        CultureInfo.InvariantCulture
      )
      : null;
}
