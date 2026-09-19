namespace Application.Features.Fleet.Models;

// One playback trail sample. Truck identity and names are sent once in FleetLocationsResponse.Trucks.
public sealed record TruckLocationPoint(
  string TruckExternalId, decimal Latitude, decimal Longitude, decimal Speed, decimal Heading, DateTime UpdatedAt);
