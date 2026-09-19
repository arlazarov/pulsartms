using Microsoft.Extensions.Primitives;

namespace API;

public static class EntityTags
{
  // Weak validators: the compressed and uncompressed bodies of one revision are equivalent.
  public static string Weak(string revision) => $"W/\"{revision}\"";

  // The first validator a poll presents, without quotes or the weak prefix; null when none is usable.
  public static string? Revision(StringValues ifNoneMatch)
  {
    foreach (var header in ifNoneMatch)
    {
      if (header is null) continue;
      foreach (var raw in header.Split(','))
      {
        var tag = raw.Trim();
        if (tag.StartsWith("W/", StringComparison.Ordinal)) tag = tag[2..];
        if (tag.Length >= 2 && tag[0] == '"' && tag[^1] == '"') return tag[1..^1];
      }
    }
    return null;
  }

  // RFC 9110 If-None-Match uses weak comparison, so W/ prefixes are ignored on both sides.
  public static bool Matches(StringValues ifNoneMatch, string revision)
  {
    foreach (var header in ifNoneMatch)
    {
      if (header is null) continue;
      foreach (var raw in header.Split(','))
      {
        var tag = raw.Trim();
        if (tag == "*") return true;
        if (tag.StartsWith("W/", StringComparison.Ordinal)) tag = tag[2..];
        if (tag.Length >= 2 && tag[0] == '"' && tag[^1] == '"' && tag[1..^1] == revision) return true;
      }
    }
    return false;
  }
}
