using Application.Features.Fleet.Interfaces;
using Application.Features.Fleet.Models;

namespace Server.Tests.Support;

public sealed class StubFleetTelemetryProvider : IFleetTelemetryProvider
{
  public sealed record StreamRequest(IReadOnlyList<string> Ids, DateTime From, DateTime To, string? Cursor);
  public List<StreamRequest> Requests { get; } = [];
  public int StatsCalls { get; private set; }
  public IReadOnlyList<VehicleTelemetry> Vehicles { get; set; } = [];
  public Func<StreamRequest, CancellationToken, Task<VehicleLocationStream>> Read { get; set; } = (_, _) => Task.FromResult(new VehicleLocationStream());
  public Task<IReadOnlyList<VehicleTelemetry>> GetVehicleTelemetryAsync(CancellationToken cancellationToken = default)
  {
    StatsCalls++;
    cancellationToken.ThrowIfCancellationRequested();
    return Task.FromResult(Vehicles);
  }
  public Task<VehicleLocationStream> GetLocationStreamAsync(IReadOnlyCollection<string> vehicleIds,
    DateTime startTime, DateTime endTime, string? cursor = null, CancellationToken cancellationToken = default)
  {
    var request = new StreamRequest(vehicleIds.ToArray(), startTime, endTime, cursor);
    Requests.Add(request);
    return Read(request, cancellationToken);
  }
}
