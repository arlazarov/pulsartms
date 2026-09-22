namespace Application.Models;

public sealed record ProcessMemoryMap(
  DateTimeOffset ObservedAt,
  RuntimeMemorySnapshot Runtime,
  string Source,
  string Status,
  IReadOnlyList<MemoryMappingGroup> Groups,
  ProcessMemoryStatus Process
);

public sealed record MemoryMappingGroup(
  string Category,
  int Mappings,
  long VirtualBytes,
  long? ResidentBytes,
  long? ProportionalBytes,
  long? AnonymousBytes,
  long? PrivateDirtyBytes
);

public sealed record ProcessMemoryStatus(
  long? ResidentBytes,
  long? AnonymousResidentBytes,
  long? FileResidentBytes,
  long? SharedResidentBytes,
  long? SwapBytes,
  long? Threads
);
