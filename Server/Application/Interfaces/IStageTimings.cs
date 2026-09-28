namespace Application.Interfaces;

// Stage timing for code outside Application, which reaches the owner
// (PerformanceStages) through this rather than the static class.
public interface IStageTimings
{
  IDisposable Start(string operation, string stage);

  void Elapsed(string operation, string stage, long started);

  void Count(string operation, string stage, long count);
}
