using System.Globalization;
using System.Text;

namespace Domain.Rules.Storage;

// How stored files read to a person outside PulsR: folder and file names
// built from a company's template, safe on every provider and filesystem.
// Names are presentation only. A file's identity is its PulsR file id and
// its provider key; renaming or moving it elsewhere never changes which
// file a message or a load refers to.
public static class StorageNaming
{
  public const int MaximumSegment = 120;
  public const string Separator = " - ";

  // A template names fields in braces; a missing field is left out together
  // with its separator, so no name ends or doubles up on " - ".
  public static readonly IReadOnlySet<string> Fields = new HashSet<string>(
    ["date", "load", "broker", "order", "truck"],
    StringComparer.Ordinal
  );

  private static readonly HashSet<string> Reserved = new(
    [
      "CON",
      "PRN",
      "AUX",
      "NUL",
      "COM1",
      "COM2",
      "COM3",
      "COM4",
      "COM5",
      "COM6",
      "COM7",
      "COM8",
      "COM9",
      "LPT1",
      "LPT2",
      "LPT3",
      "LPT4",
      "LPT5",
      "LPT6",
      "LPT7",
      "LPT8",
      "LPT9",
    ],
    StringComparer.OrdinalIgnoreCase
  );

  // Null when the template is unusable: no field, an unknown field, or
  // braces that do not close.
  public static string? Validate(string? template)
  {
    if (string.IsNullOrWhiteSpace(template) || template.Length > 200)
      return "The template must name at least one field.";
    var used = 0;
    for (var i = 0; i < template.Length; i++)
    {
      if (template[i] == '}')
        return "The template has a brace that does not open.";
      if (template[i] != '{')
        continue;
      var end = template.IndexOf('}', i);
      if (end < 0)
        return "The template has a brace that does not close.";
      if (!Fields.Contains(template[(i + 1)..end]))
        return $"The template names an unknown field: {template[(i + 1)..end]}.";
      used++;
      i = end;
    }
    return used == 0 ? "The template must name at least one field." : null;
  }

  // The template's pieces joined by the separator, skipping pieces whose
  // fields are all empty. A piece is the text between separators.
  public static string Render(
    string template,
    IReadOnlyDictionary<string, string?> values,
    string? suffix = null
  )
  {
    var pieces = new List<string>();
    foreach (var piece in template.Split(Separator))
    {
      var text = new StringBuilder();
      var filled = false;
      var empty = false;
      for (var i = 0; i < piece.Length; i++)
      {
        if (piece[i] != '{')
        {
          text.Append(piece[i]);
          continue;
        }
        var end = piece.IndexOf('}', i);
        var value =
          end < 0 ? null : values.GetValueOrDefault(piece[(i + 1)..end]);
        if (string.IsNullOrWhiteSpace(value))
          empty = true;
        else
        {
          text.Append(value.Trim());
          filled = true;
        }
        i = end < 0 ? piece.Length : end;
      }
      if (filled && !empty || !piece.Contains('{') && text.Length > 0)
        pieces.Add(text.ToString());
    }
    if (!string.IsNullOrWhiteSpace(suffix))
      pieces.Add(suffix.Trim());
    return Segment(string.Join(Separator, pieces));
  }

  // One folder or file name: normalized, no path separators, control or
  // reserved characters, no leading or trailing dots and spaces, not a
  // reserved device name, and at most MaximumSegment characters with the
  // extension kept.
  public static string Segment(string? name, string fallback = "file")
  {
    var normalized = (name ?? "").Normalize(NormalizationForm.FormC);
    var clean = new StringBuilder(normalized.Length);
    var space = false;
    foreach (var c in normalized)
    {
      var replaced =
        char.IsControl(c)
        || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'
        || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format
          ? ' '
          : c;
      if (char.IsWhiteSpace(replaced))
      {
        if (!space && clean.Length > 0)
          clean.Append(' ');
        space = true;
        continue;
      }
      clean.Append(replaced);
      space = false;
    }
    var result = clean.ToString().Trim(' ', '.');
    if (result.Length == 0)
      result = fallback;
    var stem = Path.GetFileNameWithoutExtension(result);
    if (Reserved.Contains(stem))
      result = "_" + result;
    if (result.Length > MaximumSegment)
    {
      var extension = Path.GetExtension(result);
      extension = extension.Length is > 1 and <= 10 ? extension : "";
      result =
        result[..(MaximumSegment - extension.Length)].TrimEnd(' ', '.')
        + extension;
    }
    return result;
  }

  // A name not yet taken in its folder, compared without case: "POD.pdf",
  // then "POD (2).pdf", "POD (3).pdf".
  public static string Unique(string name, IEnumerable<string> taken)
  {
    var existing = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
    if (!existing.Contains(name))
      return name;
    var extension = Path.GetExtension(name);
    var stem = name[..^extension.Length];
    for (var n = 2; ; n++)
    {
      var candidate = Segment($"{stem} ({n}){extension}");
      if (!existing.Contains(candidate))
        return candidate;
    }
  }

  // A folder path from its parts, each made safe; empty parts are dropped,
  // so no part can climb out with "..".
  public static IReadOnlyList<string> Folder(IEnumerable<string?> parts) =>
    [
      .. parts
        .SelectMany(x => (x ?? "").Split('/', '\\'))
        .Select(x => x.Trim())
        .Where(x => x.Length > 0 && x != "." && x != "..")
        .Select(x => Segment(x, "folder")),
    ];
}
