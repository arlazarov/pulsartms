using Infrastructure.Integrations.Samsara.Models;

namespace Infrastructure.Integrations.Samsara;

internal static class SamsaraLocationAddress
{
  public static string Format(SamsaraLocationSpeedAddress? address)
  {
    if (address is null)
      return string.Empty;
    var street = Join(" ", address.StreetNumber, address.Street);
    var region = Join(" ", address.State, address.PostalCode);
    var structured = Join(", ", street, address.City, region, address.Country);
    return structured.Length > 0
      ? structured
      : address.FormattedAddress?.Trim() ?? string.Empty;
  }

  private static string Join(string separator, params string?[] parts) =>
    string.Join(
      separator,
      parts.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())
    );
}
