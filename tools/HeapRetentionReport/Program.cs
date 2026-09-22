using System.Text.Json;
using Graphs;

if (args.Length != 1)
  throw new ArgumentException("Supply a local gcdump file.");
var graph = new GCHeapDump(args[0]).MemoryGraph;
var node = graph.AllocNodeStorage();
var type = graph.AllocTypeNodeStorage();
var totals = new Dictionary<string, (long Bytes, long Count)>();
var largest = new List<(int Index, int Bytes)>();
for (var i = 0; i < (int)graph.NodeIndexLimit; i++)
{
  graph.GetNode((NodeIndex)i, node);
  var name = node.GetType(type).Name;
  var previous = totals.GetValueOrDefault(name);
  totals[name] = (previous.Bytes + node.Size, previous.Count + 1);
  if (node.Size >= 100_000)
    largest.Add((i, node.Size));
}
var parents = new int[(int)graph.NodeIndexLimit];
Array.Fill(parents, -1);
var root = (int)graph.RootIndex;
parents[root] = root;
var queue = new Queue<int>();
queue.Enqueue(root);
while (queue.TryDequeue(out var at))
{
  graph.GetNode((NodeIndex)at, node);
  for (
    var child = node.GetFirstChildIndex();
    child != NodeIndex.Invalid;
    child = node.GetNextChildIndex()
  )
  {
    if (parents[(int)child] >= 0)
      continue;
    parents[(int)child] = at;
    queue.Enqueue((int)child);
  }
}
string Name(int index) =>
  graph.GetNode((NodeIndex)index, node).GetType(type).Name;
string[] RootPath(int index)
{
  var path = new List<string>();
  while (index >= 0)
  {
    path.Add(Name(index));
    if (index == root)
      break;
    index = parents[index];
  }
  path.Reverse();
  return path.ToArray();
}
Console.WriteLine(
  JsonSerializer.Serialize(
    new
    {
      graph.TotalSize,
      graph.NodeCount,
      types = totals
        .OrderByDescending(x => x.Value.Bytes)
        .Take(25)
        .Select(x => new
        {
          name = x.Key,
          x.Value.Bytes,
          x.Value.Count,
        }),
      objects = largest
        .OrderByDescending(x => x.Bytes)
        .Take(25)
        .Select(x => new
        {
          x.Bytes,
          name = Name(x.Index),
          rooted = parents[x.Index] >= 0,
          oneRootPath = RootPath(x.Index),
        }),
    },
    new JsonSerializerOptions { WriteIndented = true }
  )
);
