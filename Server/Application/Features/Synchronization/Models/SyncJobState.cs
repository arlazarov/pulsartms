namespace Application.Features.Synchronization.Models;

public sealed class SyncJobState
{
  public DateTime? LastSuccess { get; set; }
  public DateTime NextRun { get; set; }
  public int Failures { get; set; }
  public string? Error { get; set; }

  public void Success(DateTime now, int intervalSeconds)
  {
    LastSuccess = now;
    NextRun = now.AddSeconds(intervalSeconds);
    Failures = 0;
    Error = null;
  }

  public void Fail(DateTime now, int retrySeconds, string error)
  {
    Failures++;
    NextRun = now.AddSeconds(
      Math.Min(900, retrySeconds * Math.Pow(2, Math.Min(Failures - 1, 4)))
    );
    Error = error;
  }
}
