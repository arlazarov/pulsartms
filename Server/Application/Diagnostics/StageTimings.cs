namespace Application.Diagnostics;

public sealed class StageTimings : IStageTimings
{
  public IDisposable Start(string operation, string stage) =>
    PerformanceStages.Start(operation, stage);

  public void Elapsed(string operation, string stage, long started) =>
    PerformanceStages.Elapsed(operation, stage, started);

  public void Count(string operation, string stage, long count) =>
    PerformanceStages.Count(operation, stage, count);
}
