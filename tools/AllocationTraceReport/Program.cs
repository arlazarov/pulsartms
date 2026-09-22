using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

if (args.Length != 1)
  throw new ArgumentException("Supply an allocation EventPipe trace.");

var path = TraceLog.CreateFromEventPipeDataFile(args[0]);
using var log = new TraceLog(path);
var types = new Dictionary<string, long>();
var stacks = new Dictionary<string, long>();
foreach (var entry in log.Events)
{
  if (entry is not GCAllocationTickTraceData allocation)
    continue;
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
}
Console.WriteLine(
  JsonSerializer.Serialize(
    new
    {
      sampledBytes = types.Values.Sum(),
      types = types.OrderByDescending(x => x.Value).Take(20),
      stacks = stacks.OrderByDescending(x => x.Value).Take(30),
    },
    new JsonSerializerOptions { WriteIndented = true }
  )
);
