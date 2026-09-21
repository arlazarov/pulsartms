using Domain.Models.Fleet;

namespace Application.Features.Fleet.Services;

public sealed class ServerTelemetry
{
  private FleetLocationsResponse? value;
  public FleetLocationsResponse? Current => Volatile.Read(ref value);

  public void Set(FleetLocationsResponse snapshot) =>
    Volatile.Write(ref value, snapshot);
}
