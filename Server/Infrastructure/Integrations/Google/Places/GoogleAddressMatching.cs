using System.Text.RegularExpressions;

namespace Infrastructure.Integrations.Google.Places;

// Whether an address a load arrived with and one Google answered name the
// same place: street, city, state or province, postal code, US or Canada.
// Text rules only - no request, cache or response format - shared by the
// geocoder and address validation, which both judge Google's answers by
// them.
internal static class GoogleAddressMatching
{
  internal static string Street(string value, string region)
  {
    var normalized = Normalize(value);
    // State-road aliases retain both the route number and its directional
    // suffix.
    var highway = Regex.Match(
      normalized,
      @"^(\d+[A-Z]?) (?:STATE (?:HWY|ROUTE)|"
        + Regex.Escape(region)
        + @") (\d+) ?([A-Z]?)$"
    );
    return highway.Success
      ? $"{highway.Groups[1].Value} {region} {highway.Groups[2].Value}{highway.Groups[3].Value}"
      : normalized;
  }

  internal static string City(string value) =>
    Regex.Replace(Normalize(value), @"\bSAINT\b", "ST");

  internal static bool ContainsCity(string address, string city) =>
    city.Length > 0
    && (" " + City(address) + " ").Contains(
      " " + City(city) + " ",
      StringComparison.Ordinal
    );

  internal static bool MatchesRegion(
    string address,
    string region,
    string country
  )
  {
    var parts = address
      .Split(
        ',',
        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
      )
      .Skip(1)
      .Select(part => part.ToUpperInvariant())
      .ToArray();
    const string postal = @"(?:\d{5}(?:-?\d{4})?|[A-Z]\d[A-Z]\s?\d[A-Z]\d)";
    var countries = parts
      .Select(part =>
        part switch
        {
          "US" or "USA" or "UNITED STATES" => "US",
          "CANADA" => "CA",
          _ => "",
        }
      )
      .Where(value => value.Length > 0)
      .Distinct()
      .ToArray();
    var canada =
      countries.Contains("CA")
      || countries.Length == 0
        && country == "CA"
        && parts.LastOrDefault(part => !Regex.IsMatch(part, "^" + postal + "$"))
          == "CA";
    if (
      countries.Length > 1
      || countries.Length == 1 && countries[0] != country
      || canada && country != "CA"
    )
      return false;
    // Street abbreviations and country aliases cannot supply a state/province
    // token.
    var regions = parts
      .Where(part =>
        part is not ("US" or "USA" or "UNITED STATES" or "CANADA")
        && !(canada && part == "CA")
      )
      .Select(part =>
        Regex.Match(part, @"(?:^|\s)([A-Z]{2})(?:\s+" + postal + ")?$")
      )
      .Where(match => match.Success)
      .Select(match => match.Groups[1].Value)
      .Distinct()
      .ToArray();
    return regions.Length == 1 && regions[0] == region.ToUpperInvariant();
  }

  internal static bool MatchesLocality(
    string address,
    string city,
    string country,
    string postalCode
  )
  {
    // Exclude the street segment so a five-digit house number cannot become a
    // ZIP.
    var locality = string.Join(",", address.Split(',').Skip(1));
    var pattern =
      country == "CA"
        ? @"\b[A-Z]\d[A-Z]\s?\d[A-Z]\d\b"
        : @"\b\d{5}(?:-?\d{4})?\b";
    var codes = Regex
      .Matches(locality.ToUpperInvariant(), pattern)
      .Select(m => Postal(m.Value, country))
      .Distinct()
      .ToArray();
    return codes.Length == 0
      ? ContainsCity(locality, city)
      : codes.Length == 1 && codes[0] == Postal(postalCode, country);
  }

  private static string Postal(string value, string country) =>
    country == "US"
      ? Regex.Replace(value.Trim(), @"^(\d{5})(?:-?\d{4})?$", "$1")
      : value.Replace(" ", "").ToUpperInvariant();

  internal static string Normalize(string value) =>
    string.Join(
      " ",
      Regex
        .Matches(value.ToUpperInvariant(), @"[A-Z0-9]+")
        .Select(m =>
          m.Value switch
          {
            "ROAD" => "RD",
            "STREET" => "ST",
            "DRIVE" => "DR",
            "AVENUE" => "AVE",
            "BOULEVARD" => "BLVD",
            "HIGHWAY" => "HWY",
            "LANE" => "LN",
            "COURT" => "CT",
            "PARKWAY" => "PKWY",
            "WAY" => "WY",
            "NORTH" => "N",
            "SOUTH" => "S",
            "EAST" => "E",
            "WEST" => "W",
            _ => m.Value,
          }
        )
    );
}
