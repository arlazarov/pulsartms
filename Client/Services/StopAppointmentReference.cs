using System.Globalization;
using System.Text.RegularExpressions;

namespace Client.Services;

public static partial class StopAppointmentReference
{
  private const int MaximumInput = 4096;
  private const int MaximumReferences = 4;

  public static IReadOnlyList<string> Extract(string? notes, string? job)
  {
    if (string.IsNullOrEmpty(notes)) return [];
    var input = notes[..Math.Min(notes.Length, MaximumInput)];
    var kind = Kind(job);
    var references = new List<string>();
    foreach (Match match in Reference().Matches(input))
    {
      var qualifier = match.Groups[1].Value;
      var value = match.Groups[2].Value;
      if (qualifier.Length > 0 && Kind(qualifier) != kind || !IsReference(value)
        || !EndsClause(notes, match.Index + match.Length, input.Length)) continue;
      if (!references.Contains(value, StringComparer.OrdinalIgnoreCase)) references.Add(value);
      if (references.Count == MaximumReferences) break;
    }
    return references;
  }

  private static string? Kind(string? value)
  {
    var normalized = new string((value ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
    var pickup = normalized.Contains("pickup", StringComparison.Ordinal) || normalized is "pu" or "shipper";
    var delivery = normalized.Contains("delivery", StringComparison.Ordinal) || normalized.Contains("dropoff", StringComparison.Ordinal)
      || normalized is "del" or "deliver" or "receiver";
    return pickup == delivery ? null : pickup ? "pickup" : "delivery";
  }

  private static bool EndsClause(string notes, int index, int limit)
  {
    if (index < limit && notes[index] is '\'' or '"') index++;
    while (index < limit && notes[index] is ' ' or '\t') index++;
    if (index == limit) return notes.Length == limit;
    if (notes[index] is not ('.' or ',' or ';' or '\r' or '\n')) return false;
    return notes[index] is not ('.' or ',') || index + 1 == notes.Length || !char.IsAsciiLetterOrDigit(notes[index + 1]);
  }

  private static bool IsReference(string value)
  {
    if (!value.Any(char.IsAsciiDigit) || DateOrOtherId().IsMatch(value)) return false;
    if (value.Length == 4 && int.TryParse(value, out var year) && year is >= 1900 and <= 2099) return false;
    if (value.Length != 8 || !value.All(char.IsAsciiDigit)) return true;
    foreach (var format in new[] { "yyyyMMdd", "MMddyyyy", "ddMMyyyy" })
      if (DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && date.Year is >= 1900 and <= 2099) return false;
    return true;
  }

  // Only standalone labeled clauses are trusted; surrounding prose is not a fallback identifier source.
  [GeneratedRegex("(?:^|[.;,\\r\\n])\\s*(?:[-*•]\\s*)?(?:(shipper|pick\\s*up|receiver|deliver(?:y)?|drop\\s*off)(?:['’]s)?\\s*:?\\s*)?(?:appointment|appt)\\b\\s*(?:(?:confirmation|confirm|conf\\.?)\\s*(?:number|no\\.?|#)?|number|no\\.?|#)\\s*[:=#-]?\\s*[\"']?([A-Z0-9][A-Z0-9_/-]{2,63})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
  private static partial Regex Reference();
  [GeneratedRegex(@"^(?:\d{1,4}[-/_]\d{1,2}[-/_]\d{1,4}|\d{1,2}[-/]\d{1,2}|\d{1,4}(?:am|pm)|(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*[-_]?\d{1,2}[-_]?\d{2,4}|\d{1,2}[-_]?(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*[-_]?\d{2,4}|(?:bol|load|order)[-_]?\d.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
  private static partial Regex DateOrOtherId();
}
