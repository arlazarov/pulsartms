using Application.Features.Routing.Interfaces;
using Domain.Models.Routing;

namespace Server.Tests.Support;

// Stands in for the saved fuel inputs check where only its sharing is
// under test: how many operations opened a share and how many are open.
internal sealed class SharedInputsProbe : IFuelSavedInputsValidation
{
  private int open;
  private readonly TaskCompletionSource closed = new(
    TaskCreationOptions.RunContinuationsAsynchronously
  );

  public int Open => Volatile.Read(ref open);

  // Completes when the first share ends.
  public Task Closed => closed.Task;
  public int Shares { get; private set; }

  public IDisposable Share()
  {
    Shares++;
    Interlocked.Increment(ref open);
    return new Closing(this);
  }

  public Task<bool> MatchesAsync(
    FuelRecommendations access,
    CancellationToken ct
  ) => throw new NotSupportedException();

  public Task<bool> MatchesAsync(
    TruckFuelPlanSnapshot saved,
    RoutePlan current,
    CancellationToken ct
  ) => throw new NotSupportedException();

  private sealed class Closing(SharedInputsProbe owner) : IDisposable
  {
    public void Dispose()
    {
      Interlocked.Decrement(ref owner.open);
      owner.closed.TrySetResult();
    }
  }
}
