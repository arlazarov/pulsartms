using Application.Features.Fleet.Interfaces;
using Domain.Models.Fleet;

namespace Infrastructure.Integrations.Samsara;

public class SamsaraFleetProvider(
  SamsaraApiService samsaraApi,
  SamsaraDriverCatalogCache catalog
) : IFleetProvider
{
  public string Source => "samsara";

  public async Task<IReadOnlyList<ExternalDriver>> GetDriversAsync(
    CancellationToken cancellationToken = default
  )
  {
    var drivers = await catalog.GetAsync(
      samsaraApi,
      cancellationToken,
      forceRefresh: true
    );

    return
    [
      .. drivers.Select(x => new ExternalDriver
      {
        ExternalId = x.Id,
        Name = x.Name,
        FuelCard =
          x.Attributes.FirstOrDefault(a =>
              a.Name.Equals("BVD Card", StringComparison.OrdinalIgnoreCase)
            )
            ?.StringValues.FirstOrDefault() ?? string.Empty,
        IsActive = x.DriverActivationStatus.Equals(
          "active",
          StringComparison.OrdinalIgnoreCase
        ),
        Phone = x.Phone,
        Email = x.Email,
      }),
    ];
  }

  public async Task<IReadOnlyList<ExternalVehicle>> GetVehiclesAsync(
    CancellationToken cancellationToken = default
  )
  {
    var vehicles = await samsaraApi.GetVehiclesAsync(cancellationToken);

    return
    [
      .. vehicles.Select(x =>
      {
        var assetStatus = x
          .Attributes.FirstOrDefault(a =>
            a.Name.Equals("Asset Status", StringComparison.OrdinalIgnoreCase)
          )
          ?.StringValues.FirstOrDefault();

        var isActive =
          !x.Name.StartsWith("Deactivated", StringComparison.OrdinalIgnoreCase)
          && !string.Equals(
            assetStatus,
            "Scrapped",
            StringComparison.OrdinalIgnoreCase
          );

        return new ExternalVehicle
        {
          ExternalId = x.Id,
          UnitNumber = x.Name,
          Vin = x.Vin,
          IsActive = isActive,
        };
      }),
    ];
  }

  public async Task<IReadOnlyList<ExternalTrailer>> GetTrailersAsync(
    CancellationToken cancellationToken = default
  )
  {
    var trailers = await samsaraApi.GetTrailersAsync(cancellationToken);

    return
    [
      .. trailers.Select(x => new ExternalTrailer
      {
        ExternalId = x.Id,
        UnitNumber = x.Name,
        Vin = x.Vin,
        IsActive =
          !x.Name.StartsWith("Deactivated,", StringComparison.OrdinalIgnoreCase)
          && x.Tags.Any(t =>
            t.Name.Equals("AMF Carrier", StringComparison.OrdinalIgnoreCase)
          ),
      }),
    ];
  }

  public async Task<IReadOnlyList<ExternalFleetAssignment>> GetAssignmentsAsync(
    DateTime startTime,
    DateTime endTime,
    CancellationToken cancellationToken = default
  )
  {
    var assignments = await samsaraApi.GetAssignmentsAsync(
      startTime,
      endTime,
      cancellationToken
    );
    return
    [
      .. assignments.Select(x => new ExternalFleetAssignment
      {
        DriverExternalId = x.Driver.Id,
        VehicleExternalId = x.Vehicle.Id,
        VehicleName = x.Vehicle.Name,
        AssignmentType = x.AssignmentType,
        IsPassenger = x.IsPassenger,
        StartTime = x.StartTime,
        EndTime = x.EndTime,
      }),
    ];
  }

  public async Task<
    IReadOnlyList<ExternalTrailerAssignment>
  > GetTrailerAssignmentsAsync(
    IReadOnlyCollection<string> driverIds,
    CancellationToken cancellationToken = default
  )
  {
    var assignments = await samsaraApi.GetTrailerAssignmentsAsync(
      driverIds,
      cancellationToken
    );
    return
    [
      .. assignments.Select(x => new ExternalTrailerAssignment
      {
        DriverExternalId = x.Driver.DriverId,
        TrailerExternalId = x.Trailer.TrailerId,
        StartTime = x.StartTime,
        EndTime = x.EndTime,
      }),
    ];
  }
}
