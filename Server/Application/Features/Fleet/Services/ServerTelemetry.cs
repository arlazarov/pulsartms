using Application.Features.Fleet.Models;

namespace Application.Features.Fleet.Services;

public sealed class ServerTelemetry
{
  private readonly string instance = Guid.NewGuid().ToString("N")[..8];
  private long publications;
  private FleetLocationsResponse? value;
  public FleetLocationsResponse? Current => Volatile.Read(ref value);

  public void Set(FleetLocationsResponse snapshot)
  {
    // The instance prefix keeps revisions distinct across restarts and instances.
    snapshot.Revision = $"{instance}-{Interlocked.Increment(ref publications)}";
    Volatile.Write(ref value, snapshot);
  }
}
