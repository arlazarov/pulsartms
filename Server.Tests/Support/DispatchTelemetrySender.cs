using Application.Features.Fleet.Models;
using Application.Features.Fleet.Queries.GetFleetLocations;
using Application.Models;
using MediatR;

namespace Server.Tests.Support;

internal sealed class DispatchTelemetrySender(FleetLocationsResponse fleet)
  : ISender
{
  public int Calls { get; private set; }

  public Task<TResponse> Send<TResponse>(
    IRequest<TResponse> request,
    CancellationToken ct = default
  )
  {
    Assert.IsType<GetFleetLocationsQuery>(request);
    Calls++;
    return Task.FromResult(
      (TResponse)(object)RequestResponse<FleetLocationsResponse>.Ok(fleet)
    );
  }

  public Task Send<TRequest>(TRequest request, CancellationToken ct = default)
    where TRequest : IRequest => throw new NotSupportedException();

  public Task<object?> Send(object request, CancellationToken ct = default) =>
    throw new NotSupportedException();

  public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
    IStreamRequest<TResponse> request,
    CancellationToken ct = default
  ) => throw new NotSupportedException();

  public IAsyncEnumerable<object?> CreateStream(
    object request,
    CancellationToken ct = default
  ) => throw new NotSupportedException();
}
