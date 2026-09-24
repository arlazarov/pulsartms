using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

// An optional second argument skips the trace's first seconds (startup:
// JIT, model building), so a steady window is reported on its own. With
// --cpu, the sample profiler's samples of managed code running are
// counted instead of allocation ticks: about one per millisecond per
// running thread; samples of a thread waiting are left out.
var cpu = args.Contains("--cpu");

// --events lists which providers and events a trace holds, and stops.
var listing = args.Contains("--events");
args = [.. args.Where(x => x is not ("--cpu" or "--events"))];
if (args.Length is not (1 or 2))
  throw new ArgumentException(
    "Supply an EventPipe trace, optionally seconds to skip, and --cpu."
  );
var skip = args.Length == 2 ? double.Parse(args[1]) * 1000 : 0;
string[] owned = ["Application.", "Domain.", "Infrastructure.", "API."];

var path = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(path);
if (listing)
{
  foreach (
    var group in log
      .Events.GroupBy(x =>
        $"{x.ProviderName}/{x.EventName} {(x.PayloadNames.Contains("Type") ? x.PayloadByName("Type")?.GetType().Name + "=" + x.PayloadByName("Type") : "")}"
      )
      .OrderByDescending(x => x.Count())
      .Take(25)
  )
    Console.WriteLine($"{group.Count(), 9} {group.Key}");
  return;
}
var types = new Dictionary<string, long>();
var stacks = new Dictionary<string, long>();
var inner = new Dictionary<string, long>();
var outer = new Dictionary<string, long>();
double first = double.NaN,
  last = 0;
foreach (var entry in log.Events)
{
  if (entry.TimeStampRelativeMSec < skip)
    continue;
  long bytes;
  string type;
  if (cpu)
  {
    if (
      entry.ProviderName != "Microsoft-DotNETCore-SampleProfiler"
      || entry.PayloadByName("Type")?.ToString() != "Managed"
    )
      continue;
    bytes = 1;
    type = "managed sample";
  }
  else
  {
    if (entry is not GCAllocationTickTraceData allocation)
      continue;
    bytes = allocation.AllocationAmount64;
    type = allocation.TypeName ?? "unknown";
  }
  if (double.IsNaN(first))
    first = entry.TimeStampRelativeMSec;
  last = entry.TimeStampRelativeMSec;
  types[type] = types.GetValueOrDefault(type) + bytes;
  var names = new List<string>();
  for (var frame = entry.CallStack(); frame is not null; frame = frame.Caller)
  {
    var name = frame.CodeAddress.FullMethodName;
    if (!string.IsNullOrEmpty(name))
      names.Add(name);
  }
  var key = type + "\n" + string.Join("\n", names.Take(18));
  stacks[key] = stacks.GetValueOrDefault(key) + bytes;
  // The nearest and the farthest application frame: what allocated, and
  // which operation it ran under.
  var ours = names.Where(n => owned.Any(n.StartsWith)).ToList();
  var near = ours.FirstOrDefault() ?? "(no application frame)";
  var far = ours.LastOrDefault() ?? "(no application frame)";
  inner[near] = inner.GetValueOrDefault(near) + bytes;
  outer[far] = outer.GetValueOrDefault(far) + bytes;
}
Console.WriteLine(
  JsonSerializer.Serialize(
    new
    {
      sampledBytes = types.Values.Sum(),
      windowSeconds = double.IsNaN(first) ? 0 : (last - first) / 1000,
      operations = outer.OrderByDescending(x => x.Value).Take(20),
      allocators = inner.OrderByDescending(x => x.Value).Take(25),
      types = types.OrderByDescending(x => x.Value).Take(20),
      stacks = stacks.OrderByDescending(x => x.Value).Take(30),
    },
    new JsonSerializerOptions { WriteIndented = true }
  )
);
