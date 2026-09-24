using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

// An optional second argument skips the trace's first seconds (startup:
// JIT, model building), so a steady window is reported on its own.
if (args.Length is not (1 or 2))
  throw new ArgumentException(
    "Supply an allocation EventPipe trace and optionally seconds to skip."
  );
var skip = args.Length == 2 ? double.Parse(args[1]) * 1000 : 0;
string[] owned = ["Application.", "Domain.", "Infrastructure.", "API."];

var path = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(path);
var types = new Dictionary<string, long>();
var stacks = new Dictionary<string, long>();
var inner = new Dictionary<string, long>();
var outer = new Dictionary<string, long>();
double first = double.NaN,
  last = 0;
foreach (var entry in log.Events)
{
  if (
    entry is not GCAllocationTickTraceData allocation
    || entry.TimeStampRelativeMSec < skip
  )
    continue;
  if (double.IsNaN(first))
    first = entry.TimeStampRelativeMSec;
  last = entry.TimeStampRelativeMSec;
  var bytes = allocation.AllocationAmount64;
  var type = allocation.TypeName ?? "unknown";
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
