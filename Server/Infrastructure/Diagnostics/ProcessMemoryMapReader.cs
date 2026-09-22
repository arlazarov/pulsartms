using System.Diagnostics;
using Application.Interfaces;
using Application.Models;

namespace Infrastructure.Diagnostics;

public sealed class ProcessMemoryMapReader(IRuntimeMemoryReader runtime)
  : IProcessMemoryMapReader
{
  private readonly object gate = new();
  private ProcessMemoryMap? cached;
  private long capturedAt;

  public ProcessMemoryMap Read(CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    lock (gate)
    {
      ct.ThrowIfCancellationRequested();
      if (
        cached is not null
        && Stopwatch.GetElapsedTime(capturedAt) < TimeSpan.FromSeconds(30)
      )
        return cached;
      var observedAt = DateTimeOffset.UtcNow;
      var counters = runtime.Read();
      var groups = Array.Empty<MemoryMappingGroup>();
      var source = "none";
      var status = "unsupported-platform";
      var process = new ProcessMemoryStatus(null, null, null, null, null, null);
      if (OperatingSystem.IsLinux())
      {
        status = "unavailable";
        if (
          TryRead("/proc/self/status", ProcessMemoryMapParser.ParseStatus) is
          { } measured
        )
          process = measured;
        foreach (var detailed in new[] { true, false })
        {
          var path = detailed ? "/proc/self/smaps" : "/proc/self/maps";
          var parsed = TryRead(
            path,
            reader => ProcessMemoryMapParser.Parse(reader, detailed, ct)
          );
          if (parsed is null)
            continue;
          groups = parsed.ToArray();
          source = detailed ? "smaps" : "maps";
          status = detailed ? "complete" : "virtual-only";
          break;
        }
      }
      cached = new(observedAt, counters, source, status, groups, process);
      capturedAt = Stopwatch.GetTimestamp();
      return cached;
    }
  }

  private static T? TryRead<T>(string path, Func<TextReader, T> parse)
    where T : class
  {
    try
    {
      using var reader = File.OpenText(path);
      return parse(reader);
    }
    catch (InvalidDataException)
    {
      return null;
    }
    catch (IOException)
    {
      return null;
    }
    catch (UnauthorizedAccessException)
    {
      return null;
    }
    catch (OverflowException)
    {
      return null;
    }
  }
}
