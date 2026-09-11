namespace Application.Features.Routing.Models;

public sealed record RouteSection(bool Restricted, bool Unsupported, bool Truck, int? Start, int? End);
