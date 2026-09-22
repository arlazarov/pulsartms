namespace Application.Models;

public sealed record RuntimeMemorySnapshot(
  DateTimeOffset ObservedAt,
  int ProcessId,
  DateTimeOffset StartedAt,
  long WorkingSetBytes,
  long ManagedBytesEstimate,
  long TotalAllocatedBytes,
  bool ServerGc,
  long LastGcIndex,
  long HeapBytesAfterLastGc,
  long FragmentedBytesAfterLastGc,
  long CommittedBytesAfterLastGc,
  int Gen0Collections,
  int Gen1Collections,
  int Gen2Collections,
  ContainerMemorySnapshot? Container
);

public sealed record ContainerMemorySnapshot(
  long? UsageBytes,
  long? LimitBytes,
  long? AnonymousBytes,
  long? FileBytes,
  long? KernelBytes
);

public sealed record CacheMemorySnapshot(
  string Name,
  long? Entries,
  long? EstimatedSize,
  long? Limit,
  string Unit
);

public sealed record MemoryDiagnostics(
  RuntimeMemorySnapshot Runtime,
  IReadOnlyList<CacheMemorySnapshot> Caches
);
