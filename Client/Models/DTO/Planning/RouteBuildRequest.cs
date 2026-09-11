namespace Client.Models.DTO.Planning;

public sealed record RouteBuildRequest(TruckRouteProfile Profile, bool FromCurrentPosition = false, int? NextStopSequence = null);
