using System.Globalization;
using Application.Models;

namespace Infrastructure.Diagnostics;

public static class ProcessMemoryMapParser
{
  private const int MaximumCharacters = 8 * 1024 * 1024;

  public static IReadOnlyList<MemoryMappingGroup> Parse(
    TextReader reader,
    bool detailed,
    CancellationToken ct = default
  )
  {
    var groups = new Dictionary<string, MemoryMappingGroup>();
    string? category = null;
    long size = 0;
    var counters = new Dictionary<string, long>();
    var characters = 0;
    while (reader.ReadLine() is { } line)
    {
      ct.ThrowIfCancellationRequested();
      characters = checked(characters + line.Length + 1);
      if (characters > MaximumCharacters)
        throw new InvalidDataException("Memory map exceeded diagnostic limit.");
      var fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
      if (fields.Length == 0)
        continue;
      var range = fields[0].Split('-');
      if (range.Length == 2 && fields.Length >= 5)
      {
        if (
          !ulong.TryParse(
            range[0],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var start
          )
          || !ulong.TryParse(
            range[1],
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out var end
          )
          || end < start
          || end - start > long.MaxValue
        )
          throw new InvalidDataException("Invalid memory mapping range.");
        Flush();
        category = Category(fields.Length == 6 ? fields[5] : "", fields[1]);
        size = checked((long)(end - start));
        counters.Clear();
      }
      else if (detailed && category is not null)
      {
        var key = line.Split(':', 2)[0];
        if (key is "Rss" or "Pss" or "Anonymous" or "Private_Dirty")
        {
          if (Kilobytes(line) is not { } bytes || !counters.TryAdd(key, bytes))
            throw new InvalidDataException("Invalid memory mapping counter.");
        }
      }
    }
    Flush();
    if (groups.Count == 0)
      throw new InvalidDataException("No memory mappings available.");
    return groups.Values.OrderBy(x => x.Category).ToArray();

    void Flush()
    {
      if (category is null)
        return;
      long? Value(string key) =>
        counters.TryGetValue(key, out var n) ? n : null;
      var value = new MemoryMappingGroup(
        category,
        1,
        size,
        Value("Rss"),
        Value("Pss"),
        Value("Anonymous"),
        Value("Private_Dirty")
      );
      if (groups.TryGetValue(category, out var old))
        value = value with
        {
          Mappings = checked(old.Mappings + 1),
          VirtualBytes = checked(old.VirtualBytes + size),
          ResidentBytes = Add(old.ResidentBytes, value.ResidentBytes),
          ProportionalBytes = Add(
            old.ProportionalBytes,
            value.ProportionalBytes
          ),
          AnonymousBytes = Add(old.AnonymousBytes, value.AnonymousBytes),
          PrivateDirtyBytes = Add(
            old.PrivateDirtyBytes,
            value.PrivateDirtyBytes
          ),
        };
      groups[category] = value;
    }
  }

  private static long? Add(long? a, long? b) =>
    a.HasValue && b.HasValue ? checked(a.Value + b.Value) : null;

  private static string Category(string name, string permissions)
  {
    if (
      name.StartsWith("/memfd:doublemapper", StringComparison.OrdinalIgnoreCase)
    )
      return "runtime-double-mapped";
    if (name == "[heap]")
      return "labelled-process-heap";
    if (name.StartsWith("[stack", StringComparison.Ordinal))
      return "labelled-stack";
    if (name.Length == 0 || name.StartsWith("[anon", StringComparison.Ordinal))
      return permissions.Contains('x') ? "anonymous-executable" : "anonymous";
    if (name.StartsWith('['))
      return "kernel-special";
    if (name.EndsWith(" (deleted)", StringComparison.Ordinal))
      name = name[..^10];
    if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
      return "assembly-file";
    if (
      name.EndsWith(".so", StringComparison.Ordinal)
      || name.Contains(".so.", StringComparison.Ordinal)
    )
      return "native-library-file";
    return "other-file";
  }

  public static ProcessMemoryStatus ParseStatus(TextReader reader)
  {
    var values = new Dictionary<string, long?>();
    var characters = 0;
    while (reader.ReadLine() is { } line)
    {
      characters = checked(characters + line.Length + 1);
      if (characters > 64 * 1024)
        throw new InvalidDataException(
          "Process status exceeded diagnostic limit."
        );
      var pair = line.Split(':', 2);
      if (pair.Length != 2)
        continue;
      var key = pair[0];
      if (key is "VmRSS" or "RssAnon" or "RssFile" or "RssShmem" or "VmSwap")
        values[key] = Kilobytes(line);
      else if (key == "Threads")
        values[key] =
          long.TryParse(
            pair[1].Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var n
          )
          && n >= 0
            ? n
            : null;
    }
    return new(
      values.GetValueOrDefault("VmRSS"),
      values.GetValueOrDefault("RssAnon"),
      values.GetValueOrDefault("RssFile"),
      values.GetValueOrDefault("RssShmem"),
      values.GetValueOrDefault("VmSwap"),
      values.GetValueOrDefault("Threads")
    );
  }

  private static long? Kilobytes(string line)
  {
    var fields = line.Split(
      (char[]?)null,
      StringSplitOptions.RemoveEmptyEntries
    );
    return
      fields.Length == 3
      && fields[2] == "kB"
      && long.TryParse(
        fields[1],
        NumberStyles.None,
        CultureInfo.InvariantCulture,
        out var value
      )
      && value <= long.MaxValue / 1024
      ? value * 1024
      : null;
  }
}
