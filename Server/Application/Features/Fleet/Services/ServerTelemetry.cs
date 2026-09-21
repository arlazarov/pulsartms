using System.Collections.Concurrent;
using Application.Interfaces;
using Domain.Models.Fleet;

namespace Application.Features.Fleet.Services;

public sealed class ServerTelemetry(ICurrentCompany companies)
{
  private readonly ConcurrentDictionary<Guid, FleetLocationsResponse> values =
    new();

  public FleetLocationsResponse? Current =>
    companies.Id is { } company && values.TryGetValue(company, out var value)
      ? value
      : null;

  public void Set(FleetLocationsResponse snapshot)
  {
    var company =
      companies.Id
      ?? throw new InvalidOperationException("Telemetry requires a company.");
    values[company] = snapshot;
  }
}
