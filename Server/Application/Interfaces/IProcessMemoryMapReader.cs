using Application.Models;

namespace Application.Interfaces;

public interface IProcessMemoryMapReader
{
  ProcessMemoryMap Read(CancellationToken ct);
}
