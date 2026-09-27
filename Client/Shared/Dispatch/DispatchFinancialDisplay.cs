using System.Globalization;

namespace Client.Shared.Dispatch;

public static class DispatchFinancialDisplay
{
  public static string Miles(decimal? value) =>
    value is >= 0
      ? $"{value.Value.ToString("N0", CultureInfo.InvariantCulture)} mi"
      : "—";

  // A rate beside money already named in its currency: the number alone.
  public static string Amount(decimal? value) =>
    value is >= 0
      ? value.Value.ToString("N2", CultureInfo.InvariantCulture)
      : "—";

  public static string Money(decimal? value, string? currency) =>
    value is >= 0
      ? string.Join(
        " ",
        new[]
        {
          value.Value.ToString("N2", CultureInfo.InvariantCulture),
          currency?.Trim().ToUpperInvariant() ?? "",
        }.Where(text => text.Length > 0)
      )
      : "—";
}
