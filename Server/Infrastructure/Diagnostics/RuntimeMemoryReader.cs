using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using Application.Interfaces;
using Application.Models;

namespace Infrastructure.Diagnostics;

public sealed class RuntimeMemoryReader : IRuntimeMemoryReader
{
  public RuntimeMemorySnapshot Read()
  {
    using var process = Process.GetCurrentProcess();
    var gc = GC.GetGCMemoryInfo();
    return new(
      DateTimeOffset.UtcNow,
      process.Id,
      process.StartTime.ToUniversalTime(),
      process.WorkingSet64,
      GC.GetTotalMemory(false),
      GC.GetTotalAllocatedBytes(),
      GCSettings.IsServerGC,
      gc.Index,
      gc.HeapSizeBytes,
      gc.FragmentedBytes,
      gc.TotalCommittedBytes,
      GC.CollectionCount(0),
      GC.CollectionCount(1),
      GC.CollectionCount(2),
      OperatingSystem.IsLinux() ? ReadContainer() : null
    );
  }

  private static ContainerMemorySnapshot? ReadContainer()
  {
    var root = "/sys/fs/cgroup/";
    var usage = ReadFile(root + "memory.current");
    var v2 = usage is not null;
    if (!v2)
    {
      root += "memory/";
      usage = ReadFile(root + "memory.usage_in_bytes");
    }
    var limit = ReadFile(root + (v2 ? "memory.max" : "memory.limit_in_bytes"));
    var stats = ReadFile(root + "memory.stat");
    if (usage is null && limit is null && stats is null)
      return null;
    return ParseContainer(usage, limit, stats, v2);
  }

  public static ContainerMemorySnapshot ParseContainer(
    string? usage,
    string? limit,
    string? stats,
    bool v2
  )
  {
    var values = new Dictionary<string, long>();
    foreach (var line in (stats ?? "").Split('\n'))
    {
      var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 2 && Number(parts[1]) is { } value)
        values[parts[0]] = value;
    }
    long? Find(string key) => values.TryGetValue(key, out var n) ? n : null;
    var maximum = Number(limit);
    return new(
      Number(usage),
      maximum is >= long.MaxValue / 2 ? null : maximum,
      Find(v2 ? "anon" : "total_rss"),
      Find(v2 ? "file" : "total_cache"),
      v2 ? Find("kernel") : null
    );
  }

  private static long? Number(string? value) =>
    long.TryParse(
      value?.Trim(),
      NumberStyles.None,
      CultureInfo.InvariantCulture,
      out var n
    )
    && n >= 0
      ? n
      : null;

  private static string? ReadFile(string path)
  {
    try
    {
      return File.Exists(path) ? File.ReadAllText(path) : null;
    }
    catch (IOException)
    {
      return null;
    }
    catch (UnauthorizedAccessException)
    {
      return null;
    }
  }
}
