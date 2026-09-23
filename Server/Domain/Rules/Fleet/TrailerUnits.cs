namespace Domain.Rules.Fleet;

// A trailer's number as every source writes it: trimmed and upper case, and
// at most ten characters - the rule the catalog already applied to telemetry
// names. A number outside that is not a trailer number and names nothing.
public static class TrailerUnits
{
  public const int MaximumLength = 10;

  public static string? Normalize(string? unit)
  {
    var text = unit?.Trim().ToUpperInvariant();
    return
      string.IsNullOrEmpty(text)
      || text.Length > MaximumLength
      || text.Any(char.IsControl)
      ? null
      : text;
  }

  // Whether a load's trailer field names a trailer at all: "TBD" or "N/A"
  // does not, so it is never catalogued.
  public static string? Nameable(string? unit) =>
    Normalize(unit) is { } text && text.Any(char.IsAsciiDigit) ? text : null;

  public static string Vin(string? vin)
  {
    var text = vin?.Trim().ToUpperInvariant() ?? "";
    return text.Length == 17 && text.All(char.IsAsciiLetterOrDigit) ? text : "";
  }
}
