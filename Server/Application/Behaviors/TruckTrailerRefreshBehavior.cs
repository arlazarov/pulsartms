using Application.Caching;
using Application.Features.Fleet.Services;
using Application.Models;
using Microsoft.Extensions.Logging;

namespace Application.Behaviors;

// When a request commits a change to current work, the trailers of the
// trucks it touched are resolved before it answers - not two minutes later
// at the next synchronization, which stays as recovery for anything this
// misses. Only those trucks are read, and nothing calls a provider or plans
// fuel. A request that failed changed nothing and refreshes nothing. A
// refresh that fails leaves the committed change as it is.
public sealed class TruckTrailerRefreshBehavior<TRequest, TResponse>(
  IAppDbContext db,
  ReadCache reads,
  ILogger<TruckTrailerRefreshBehavior<TRequest, TResponse>> logger
) : IPipelineBehavior<TRequest, TResponse>
  where TRequest : notnull
{
  public async Task<TResponse> Handle(
    TRequest request,
    RequestHandlerDelegate<TResponse> next,
    CancellationToken ct
  )
  {
    using var collecting = TruckWorkChanges.Collect(out var marked);
    var response = await next();
    var trucks = TruckWorkChanges.Take(marked);
    if (trucks.Length == 0 || response is IRequestOutcome { Success: false })
      return response;
    try
    {
      await TruckTrailerAssignments.RefreshTrucksAsync(db, reads, trucks, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      logger.LogWarning(
        ex,
        "Trailer refresh after {Operation} failed; the next synchronization "
          + "retries it",
        typeof(TRequest).Name
      );
    }
    return response;
  }
}
