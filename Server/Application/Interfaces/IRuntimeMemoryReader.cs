using Application.Models;

namespace Application.Interfaces;

public interface IRuntimeMemoryReader
{
  RuntimeMemorySnapshot Read();
}

public interface ICacheMemorySource
{
  IReadOnlyList<CacheMemorySnapshot> ReadMemory();
}
