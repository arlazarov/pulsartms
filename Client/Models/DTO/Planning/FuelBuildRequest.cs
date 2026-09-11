namespace Client.Models.DTO.Planning;

public sealed record FuelBuildRequest(TruckRouteProfile Profile, double? CurrentGallons = null);
