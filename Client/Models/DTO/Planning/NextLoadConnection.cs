namespace Client.Models.DTO.Planning;

public sealed record NextLoadConnection(double Miles, IReadOnlyList<RoutePoint> Points);
