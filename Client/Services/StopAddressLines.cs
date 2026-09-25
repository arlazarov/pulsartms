using System.Text.RegularExpressions;

namespace Client.Services;

public sealed partial record StopAddressLines(string Street, string Locality)
{
  private static readonly HashSet<string> Regions = new(
    (
      "AL AK AZ AR CA CO CT DE DC FL GA HI ID IL IN IA KS KY LA ME MD MA MI MN MS MO MT NE NV NH NJ NM NY NC ND OH OK OR PA RI SC SD TN TX UT VT VA WA WV WI WY AS GU MP PR VI "
      + "AB BC MB NB NL NS NT NU ON PE QC SK YT"
    ).Split(' '),
    StringComparer.OrdinalIgnoreCase
  );
  private static readonly HashSet<string> Countries = new(
    ["US", "USA", "United States", "Canada", "CA"],
    StringComparer.OrdinalIgnoreCase
  );

  public static StopAddressLines Create(string? address)
  {
    var parts = (address ?? "").Split(
      ',',
      StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
    );
    // CA can be either a country suffix or the California region.
    if (
      parts.Length > 0
      && Countries.Contains(parts[^1])
      && TrySplit(parts, parts.Length - 2, parts[^1], out var withCountry)
    )
      return withCountry;
    return TrySplit(parts, parts.Length - 1, null, out var result)
      ? result
      : new(string.Join(", ", parts), "");
  }

  private static bool TrySplit(
    string[] parts,
    int end,
    string? country,
    out StopAddressLines result
  )
  {
    result = new("", "");
    if (end < 2)
      return false;
    var postal = "";
    var combined = RegionPostal().Match(parts[end]);
    string region;
    if (combined.Success)
    {
      region = combined.Groups[1].Value;
      postal = combined.Groups[2].Value;
      end--;
    }
    else
    {
      if (Postal().IsMatch(parts[end]))
        postal = parts[end--];
      if (end < 2)
        return false;
      region = parts[end--];
    }
    if (!Regions.Contains(region) || end < 1 || !IsCity(parts[end]))
      return false;
    var locality = new[]
    {
      parts[end],
      string.Join(" ", new[] { region, postal }.Where(x => x.Length > 0)),
      country,
    };
    // A source address often carries its own city, region, postal code
    // and country before the structured ones repeat them: those repeats
    // leave the street line, which keeps at least one part. Only exact
    // repeats go; the full address is still what is copied.
    var street = parts.Take(end).ToList();
    var repeats = new HashSet<string>(
      new[] { parts[end], region, postal, $"{region} {postal}" }.Where(x =>
        x.Trim().Length > 0
      ),
      StringComparer.OrdinalIgnoreCase
    );
    while (
      street.Count > 1
      && (
        repeats.Contains(street[^1])
        || country is not null && SameCountry(street[^1], country)
      )
    )
      street.RemoveAt(street.Count - 1);
    result = new(
      string.Join(", ", street),
      string.Join(", ", locality.Where(x => !string.IsNullOrEmpty(x)))
    );
    return true;
  }

  // US and USA, or the same word: one country. "CA" is left as written,
  // since it may be California.
  private static bool SameCountry(string value, string country) =>
    value.Equals(country, StringComparison.OrdinalIgnoreCase)
    || UnitedStates.Contains(value) && UnitedStates.Contains(country);

  private static readonly HashSet<string> UnitedStates = new(
    ["US", "USA", "United States"],
    StringComparer.OrdinalIgnoreCase
  );

  private static bool IsCity(string value) =>
    value.Any(char.IsLetter)
    && !char.IsDigit(value[0])
    && !Regions.Contains(value)
    && !Countries.Contains(value)
    && !Postal().IsMatch(value)
    && !RegionPostal().IsMatch(value)
    && !Unit().IsMatch(value);

  [GeneratedRegex(
    @"^(?:\d{5}(?:-\d{4})?|[A-Z]\d[A-Z]\s?\d[A-Z]\d)$",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
  )]
  private static partial Regex Postal();

  [GeneratedRegex(
    @"^([A-Z]{2})\s*(\d{5}(?:-\d{4})?|[A-Z]\d[A-Z]\s?\d[A-Z]\d)$",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
  )]
  private static partial Regex RegionPostal();

  [GeneratedRegex(
    @"^(?:apt|apartment|unit|suite|ste|building|bldg|floor|#)(?:\b|\s|\d)",
    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
  )]
  private static partial Regex Unit();
}
