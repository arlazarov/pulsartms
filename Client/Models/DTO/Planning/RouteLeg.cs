namespace Client.Models.DTO.Planning;

public sealed record RouteLeg(double Miles, double Seconds, List<RoutePoint> Points);
