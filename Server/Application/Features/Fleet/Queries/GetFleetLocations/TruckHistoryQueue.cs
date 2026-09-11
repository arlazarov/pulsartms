using System.Collections.Concurrent;
using System.Threading.Channels;
namespace Application.Features.Fleet.Queries.GetFleetLocations;
public sealed class TruckHistoryQueue
{
  private readonly Channel<GetTruckHistoryQuery> requests = Channel.CreateBounded<GetTruckHistoryQuery>(100);
  private readonly ConcurrentDictionary<GetTruckHistoryQuery, byte> pending = new();
  public void Enqueue(GetTruckHistoryQuery query)
  {
    if (pending.TryAdd(query, 0) && !requests.Writer.TryWrite(query)) pending.TryRemove(query, out _);
  }
  public IAsyncEnumerable<GetTruckHistoryQuery> ReadAsync(CancellationToken ct) => requests.Reader.ReadAllAsync(ct);
  public void Complete(GetTruckHistoryQuery query) => pending.TryRemove(query, out _);
}
