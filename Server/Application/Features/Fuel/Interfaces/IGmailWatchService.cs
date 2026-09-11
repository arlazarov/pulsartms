namespace Application.Features.Fuel.Interfaces;

public interface IGmailWatchService
{
  Task<GmailWatchResult> StartAsync(CancellationToken cancellationToken = default);
}

public class GmailWatchResult
{
  public ulong? HistoryId { get; set; }

  public long? Expiration { get; set; }
}
