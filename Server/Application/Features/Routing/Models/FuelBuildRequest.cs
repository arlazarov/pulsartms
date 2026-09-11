namespace Application.Features.Routing.Models;

public sealed record FuelBuildRequest(TruckRouteProfile Profile, double? CurrentGallons = null);
