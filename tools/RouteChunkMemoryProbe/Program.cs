using System.Text.Json;
using Infrastructure.Diagnostics;

if (args is ["--memory-map-local"])
{
  var reader = new ProcessMemoryMapReader(new RuntimeMemoryReader());
  Console.WriteLine(JsonSerializer.Serialize(reader.Read(default)));
  return;
}
if (args is ["--preview-read-only"])
{
  await ReadCycleProbe.RunAsync();
  return;
}
if (
  args.Length != 1
  || args[0] is not ("--read-only" or "--allocations-read-only")
)
  throw new ArgumentException("Choose a read-only measurement mode.");
await ChunkMemoryProbe.RunAsync(args[0] == "--allocations-read-only");
